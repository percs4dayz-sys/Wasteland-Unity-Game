using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace Wasteland.EditorTools
{
    /// <summary>
    /// A Qwen (Alibaba Model Studio) chat assistant that docks inside the Unity Editor.
    /// Open via  Wasteland ▸ Qwen Assistant.  Talks to the OpenAI-compatible endpoint
    /// of your MaaS deployment. Key/model/URL live in EditorPrefs (never committed).
    ///
    /// This is a CHAT assistant (ask questions, get code/answers). It does not yet
    /// manipulate the scene by itself — that agentic layer is a possible phase 2.
    /// </summary>
    public class QwenAssistantWindow : EditorWindow
    {
        // ---- EditorPrefs keys (per project) ----
        const string PK_Key    = "Wasteland.Qwen.ApiKey";
        const string PK_Url    = "Wasteland.Qwen.BaseUrl";
        const string PK_Model  = "Wasteland.Qwen.Model";
        const string PK_System = "Wasteland.Qwen.System";

        const string DefaultUrl = "https://ws-65kxvob3irgcdak2.ap-southeast-1.maas.aliyuncs.com/compatible-mode/v1";
        static readonly string[] Models = { "qwen-max", "qwen-plus", "qwen3-vl-plus" };

        string apiKey, baseUrl, model, systemPrompt;
        string input = "";
        Vector2 scroll;
        bool showSettings;
        bool sending;

        readonly List<Msg> messages = new List<Msg>();

        // cross-thread handoff (background thread fills these; main thread consumes)
        readonly object gate = new object();
        string pendingRaw;      // raw HTTP body on success
        string pendingError;    // error text on failure
        bool pendingReady;

        static readonly HttpClient http = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };

        struct Msg { public string role; public string content; }

        [MenuItem("Wasteland/Qwen Assistant")]
        public static void Open()
        {
            var w = GetWindow<QwenAssistantWindow>("Qwen");
            w.minSize = new Vector2(320, 300);
            w.Show();
        }

        void OnEnable()
        {
            apiKey       = EditorPrefs.GetString(PK_Key, "");
            baseUrl      = EditorPrefs.GetString(PK_Url, DefaultUrl);
            model        = EditorPrefs.GetString(PK_Model, "qwen-max");
            systemPrompt = EditorPrefs.GetString(PK_System, "You are a helpful assistant embedded in the Unity editor of a game project. Be concise.");
            showSettings = string.IsNullOrEmpty(apiKey);
        }

        void OnInspectorUpdate()
        {
            // pump background results on the main thread
            if (!pendingReady) return;
            string raw, err;
            lock (gate) { raw = pendingRaw; err = pendingError; pendingReady = false; pendingRaw = pendingError = null; }

            sending = false;
            if (err != null) { AddMessage("error", err); }
            else
            {
                string reply = ParseReply(raw, out string perr);
                AddMessage(perr != null ? "error" : "assistant", perr ?? reply);
            }
            Repaint();
        }

        void OnGUI()
        {
            DrawSettings();
            DrawTranscript();
            DrawInput();
        }

        void DrawSettings()
        {
            showSettings = EditorGUILayout.Foldout(showSettings, "Settings", true);
            if (!showSettings) return;

            EditorGUI.indentLevel++;
            EditorGUI.BeginChangeCheck();
            apiKey = EditorGUILayout.PasswordField("API Key", apiKey);
            int mi = Mathf.Max(0, Array.IndexOf(Models, model));
            mi = EditorGUILayout.Popup("Model", mi, Models);
            model = Models[Mathf.Clamp(mi, 0, Models.Length - 1)];
            baseUrl = EditorGUILayout.TextField("Base URL", baseUrl);
            EditorGUILayout.LabelField("System Prompt");
            systemPrompt = EditorGUILayout.TextArea(systemPrompt, GUILayout.MinHeight(38));
            if (EditorGUI.EndChangeCheck())
            {
                EditorPrefs.SetString(PK_Key, apiKey);
                EditorPrefs.SetString(PK_Url, baseUrl);
                EditorPrefs.SetString(PK_Model, model);
                EditorPrefs.SetString(PK_System, systemPrompt);
            }
            if (string.IsNullOrEmpty(apiKey))
                EditorGUILayout.HelpBox("Paste your Qwen/DashScope key (sk-...) to start.", MessageType.Info);
            EditorGUI.indentLevel--;
            EditorGUILayout.Space();
        }

        void DrawTranscript()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll, "box", GUILayout.ExpandHeight(true));
            foreach (var m in messages)
            {
                var style = new GUIStyle(EditorStyles.wordWrappedLabel) { richText = true };
                string who = m.role == "user" ? "<b>You</b>" :
                             m.role == "assistant" ? "<b>Qwen</b>" :
                             "<b><color=#e06666>Error</color></b>";
                EditorGUILayout.LabelField($"{who}\n{m.content}", style);
                EditorGUILayout.Space(2);
            }
            if (sending) EditorGUILayout.LabelField("<i>Qwen is thinking…</i>", new GUIStyle(EditorStyles.label){richText=true});
            EditorGUILayout.EndScrollView();
        }

        void DrawInput()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Clear", GUILayout.Width(50))) { messages.Clear(); }
                GUI.enabled = !sending && !string.IsNullOrWhiteSpace(input) && !string.IsNullOrEmpty(apiKey);
                if (GUILayout.Button(sending ? "…" : "Send", GUILayout.Width(60))) Send();
                GUI.enabled = true;
            }
            input = EditorGUILayout.TextArea(input, GUILayout.MinHeight(46));
            EditorGUILayout.LabelField("Tip: Send button submits. Ctrl+Enter also works.", EditorStyles.miniLabel);

            var e = Event.current;
            if (e.type == EventType.KeyDown && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)
                && e.control && !sending && !string.IsNullOrWhiteSpace(input) && !string.IsNullOrEmpty(apiKey))
            {
                Send(); e.Use();
            }
        }

        void AddMessage(string role, string content)
        {
            messages.Add(new Msg { role = role, content = content });
            scroll.y = float.MaxValue;
        }

        void Send()
        {
            string userText = input.Trim();
            if (string.IsNullOrEmpty(userText)) return;
            input = "";
            GUI.FocusControl(null);
            AddMessage("user", userText);
            sending = true;

            // build request JSON on the MAIN thread (JsonUtility is main-thread only)
            string json = BuildRequestJson();
            string url = baseUrl.TrimEnd('/') + "/chat/completions";
            string key = apiKey;
            _ = PostAsync(url, key, json);
        }

        async Task PostAsync(string url, string key, string json)
        {
            try
            {
                using (var req = new HttpRequestMessage(HttpMethod.Post, url))
                {
                    req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + key);
                    req.Content = new StringContent(json, Encoding.UTF8, "application/json");
                    var resp = await http.SendAsync(req).ConfigureAwait(false);
                    string body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                    lock (gate)
                    {
                        if (resp.IsSuccessStatusCode) { pendingRaw = body; pendingError = null; }
                        else { pendingRaw = null; pendingError = $"HTTP {(int)resp.StatusCode}: {body}"; }
                        pendingReady = true;
                    }
                }
            }
            catch (Exception ex)
            {
                lock (gate) { pendingError = ex.Message; pendingRaw = null; pendingReady = true; }
            }
        }

        // ---- JSON (JsonUtility, main thread) ----
        [Serializable] class WireMsg { public string role; public string content; }
        [Serializable] class ReqDto { public string model; public WireMsg[] messages; }
        [Serializable] class RespDto { public Choice[] choices; [Serializable] public class Choice { public WireMsg message; } }

        string BuildRequestJson()
        {
            var list = new List<WireMsg>();
            if (!string.IsNullOrWhiteSpace(systemPrompt)) list.Add(new WireMsg { role = "system", content = systemPrompt });
            foreach (var m in messages)
                if (m.role == "user" || m.role == "assistant")
                    list.Add(new WireMsg { role = m.role, content = m.content });
            return JsonUtility.ToJson(new ReqDto { model = model, messages = list.ToArray() });
        }

        static string ParseReply(string raw, out string error)
        {
            error = null;
            try
            {
                var r = JsonUtility.FromJson<RespDto>(raw);
                if (r?.choices != null && r.choices.Length > 0 && r.choices[0].message != null)
                    return r.choices[0].message.content;
                error = "No reply in response:\n" + raw;
            }
            catch (Exception e) { error = "Parse error: " + e.Message + "\n" + raw; }
            return null;
        }
    }
}
