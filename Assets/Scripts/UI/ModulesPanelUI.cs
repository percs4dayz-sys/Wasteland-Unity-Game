using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using System.Collections.Generic;

/// <summary>Live socket editor. Select an equipped socket, then install or remove a carried module.</summary>
public class ModulesPanelUI : MonoBehaviour
{
    public static ModulesPanelUI Instance { get; private set; }
    GameObject _backdrop, _panel;
    RectTransform _choices;
    TextMeshProUGUI _status;
    readonly List<(string slot,int index,Button button,TextMeshProUGUI text,Image icon)> _sockets=new();
    PlayerEntity _bound;
    string _slot="Weapon";
    int _index;
    public bool IsOpen => _backdrop != null && _backdrop.activeSelf;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap() { if(Instance==null)new GameObject("ModulesPanelUI (auto)").AddComponent<ModulesPanelUI>(); }
    void Awake()
    {
        if(Instance!=null && Instance!=this){Destroy(gameObject);return;}
        Instance=this;DontDestroyOnLoad(gameObject);BuildUI();Close();
    }
    void Update()
    {
        if(Input.GetKeyDown(KeyCode.M) && !ChatInput.IsTyping)Toggle();
        if(IsOpen && Input.GetKeyDown(KeyCode.Escape))Close();
        if(IsOpen && _bound!=PlayerEntity.Instance)Bind();
    }
    void Bind()
    {
        if(_bound!=null){_bound.Inventory.OnChanged-=Refresh;_bound.Equipment.OnChanged-=Refresh;}
        _bound=PlayerEntity.Instance;
        if(_bound!=null){_bound.Inventory.OnChanged+=Refresh;_bound.Equipment.OnChanged+=Refresh;}
        Refresh();
    }
    void OnDestroy(){if(_bound!=null){_bound.Inventory.OnChanged-=Refresh;_bound.Equipment.OnChanged-=Refresh;}}
    public void Toggle(){if(IsOpen)Close();else Open();}
    public void Open(){_backdrop.SetActive(true);_panel.transform.SetAsLastSibling();Bind();}
    public void Close(){if(_backdrop!=null)_backdrop.SetActive(false);}
    void Choose(string slot,int index){_slot=slot;_index=index;Refresh();}
    void Install(int id){_status.text=ModuleCatalog.Socket(_bound,_slot,_index,id);Refresh();}
    void Refresh()
    {
        if(!IsOpen)return;
        foreach(var cell in _sockets)
        {
            var gear=_bound?.Equipment.GetItem(cell.slot);
            var modules=_bound?.Equipment.GetModules(cell.slot);
            int id=modules!=null && cell.index<modules.Length?modules[cell.index]:0;
            cell.button.interactable=gear!=null;
            cell.text.text=gear==null?"Equip gear":id==0?"+ Empty socket":ItemRegistry.Get(id)?.name;
            cell.icon.sprite=id==0?null:ItemIconLoader.LoadIcon(id);
            cell.icon.enabled=gear!=null && cell.icon.sprite!=null;
            cell.text.margin=new Vector4(cell.icon.enabled?38:4,2,4,2);
            cell.button.image.color=cell.slot==_slot && cell.index==_index ? new Color(.42f,.32f,.14f) : new Color(.17f,.16f,.13f);
        }
        for(int i=_choices.childCount-1;i>=0;i--) { var child=_choices.GetChild(i);child.gameObject.SetActive(false);child.SetParent(null);Destroy(child.gameObject); }
        if(_bound==null)return;
        var worn=_bound.Equipment.GetItem(_slot);
        if(worn==null){_status.text="Equip gear, then select a socket.";return;}
        var installed=_bound.Equipment.GetModules(_slot);
        int current=_index<installed.Length?installed[_index]:0;
        if(current!=0)Choice("Remove "+ItemRegistry.Get(current)?.name,"Returns the module to your inventory.",()=>Install(0),current);
        var seen=new HashSet<int>();
        for(int i=0;i<Inventory.MAX;i++)
        {
            var stack=_bound.Inventory.GetSlot(i);
            if(stack==null || !seen.Add(stack.itemId) || !ModuleCatalog.Fits(stack.itemId,_slot))continue;
            int id=stack.itemId;
            var item=stack.Item;
            Choice(item.name+"  x"+_bound.Inventory.CountOf(id),item.description,()=>Install(id),id);
        }
        if(_choices.childCount==0)Choice("No compatible modules in your pack","Find modules on enemies, or upgrade three matching Common modules at a workbench.",null);
    }
    void Choice(string name,string description,UnityEngine.Events.UnityAction action,int itemId=0)
    {
        var row=NewUI("Module",_choices);
        row.gameObject.AddComponent<LayoutElement>().preferredHeight=110;
        var bg=row.gameObject.AddComponent<Image>();bg.color=new Color(.17f,.16f,.13f);
        var button=row.gameObject.AddComponent<Button>();button.targetGraphic=bg;button.interactable=action!=null;
        if(action!=null)button.onClick.AddListener(action);
        var text=Label(row,name+"\n<size=16><color=#C8C0AE>"+description+"</color></size>",15);
        var icon=Icon(row,8,8,48);
        icon.sprite=itemId==0?null:ItemIconLoader.LoadIcon(itemId);icon.enabled=icon.sprite!=null;
        text.margin=new Vector4(icon.enabled?64:8,5,8,5);text.alignment=TextAlignmentOptions.TopLeft;
    }
    void BuildUI()
    {
        if(Object.FindAnyObjectByType<EventSystem>()==null)
            DontDestroyOnLoad(new GameObject("EventSystem",typeof(EventSystem),typeof(StandaloneInputModule)));
        var canvasGo=new GameObject("ModulesCanvas",typeof(Canvas),typeof(CanvasScaler),typeof(GraphicRaycaster));
        canvasGo.transform.SetParent(transform,false);
        var canvas=canvasGo.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceOverlay;canvas.sortingOrder=525;
        var scaler=canvasGo.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1920,1080);
        var backdrop=NewUI("Backdrop",canvasGo.transform);Stretch(backdrop);_backdrop=backdrop.gameObject;
        var panel=NewUI("Panel",backdrop);panel.sizeDelta=new Vector2(520,640);UITheme.DockSidePanel(panel);_panel=panel.gameObject;
        panel.gameObject.AddComponent<Image>().color=new Color(.10f,.10f,.08f,.99f);
        var title=NewUI("Title",panel);Place(title,12,6,450,30);Label(title,"GEAR MODULES",19);
        ButtonAt(panel,"X",474,6,32,30,Close);
        string[] slots={"Weapon","Helmet","Chest","Legs","Shield"};
        float y=48;
        foreach(string slot in slots)
        {
            var label=NewUI(slot,panel);Place(label,12,y,100,44);Label(label,slot,15);
            int count=slot=="Weapon"||slot=="Chest"?2:1;
            for(int i=0;i<count;i++)
            {
                int idx=i;string key=slot;
                var button=ButtonAt(panel,"+",118+i*192,y,184,44,()=>Choose(key,idx));
                var text=button.GetComponentInChildren<TextMeshProUGUI>();text.fontSize=15;
                _sockets.Add((slot,i,button,text,Icon(button.transform,4,6,32)));
            }
            y+=50;
        }
        var status=NewUI("Status",panel);Place(status,12,302,496,42);_status=Label(status,"Select a socket. Modules stay with that piece of gear.",13);
        var viewport=NewUI("ModuleChoices",panel);Place(viewport,12,350,496,276);
        viewport.gameObject.AddComponent<Image>().color=new Color(.07f,.07f,.06f);viewport.gameObject.AddComponent<RectMask2D>();
        _choices=NewUI("Content",viewport);_choices.anchorMin=new Vector2(0,1);_choices.anchorMax=Vector2.one;_choices.pivot=new Vector2(.5f,1);_choices.sizeDelta=Vector2.zero;
        var layout=_choices.gameObject.AddComponent<VerticalLayoutGroup>();layout.spacing=6;layout.childControlHeight=true;layout.childForceExpandHeight=false;layout.padding=new RectOffset(4,4,4,4);
        _choices.gameObject.AddComponent<ContentSizeFitter>().verticalFit=ContentSizeFitter.FitMode.PreferredSize;
        var scroll=viewport.gameObject.AddComponent<ScrollRect>();scroll.viewport=viewport;scroll.content=_choices;scroll.horizontal=false;scroll.scrollSensitivity=32;
    }
    static Button ButtonAt(Transform parent,string name,float x,float y,float w,float h,UnityEngine.Events.UnityAction action)
    {
        var rt=NewUI(name,parent);Place(rt,x,y,w,h);var image=rt.gameObject.AddComponent<Image>();image.color=new Color(.25f,.22f,.15f);
        var b=rt.gameObject.AddComponent<Button>();b.targetGraphic=image;b.onClick.AddListener(action);Label(rt,name,15);return b;
    }
    static TextMeshProUGUI Label(Transform parent,string text,float size)
    {
        var rt=NewUI("Label",parent);Stretch(rt);var t=rt.gameObject.AddComponent<TextMeshProUGUI>();
        t.text=text;t.fontSize=size;t.color=Color.white;t.alignment=TextAlignmentOptions.Center;t.raycastTarget=false;return t;
    }
    static Image Icon(Transform parent,float x,float y,float size)
    {
        var rt=NewUI("ItemIcon",parent);Place(rt,x,y,size,size);
        var icon=rt.gameObject.AddComponent<Image>();icon.preserveAspect=true;icon.raycastTarget=false;icon.enabled=false;return icon;
    }
    static RectTransform NewUI(string name,Transform parent){var go=new GameObject(name,typeof(RectTransform));go.transform.SetParent(parent,false);return go.GetComponent<RectTransform>();}
    static void Place(RectTransform rt,float x,float y,float w,float h){rt.anchorMin=rt.anchorMax=new Vector2(0,1);rt.pivot=new Vector2(0,1);rt.anchoredPosition=new Vector2(x,-y);rt.sizeDelta=new Vector2(w,h);}
    static void Stretch(RectTransform rt){rt.anchorMin=Vector2.zero;rt.anchorMax=Vector2.one;rt.offsetMin=rt.offsetMax=Vector2.zero;}
}
