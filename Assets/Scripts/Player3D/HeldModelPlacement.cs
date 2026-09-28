using UnityEngine;

/// <summary>Authored handle points for the shipped models, measured in their imported bounds.
/// Local normalized coordinates survive the very different import scales and off-centre pivots.
/// Forward is the blade/barrel direction; Up chooses its roll. Source assets stay untouched.</summary>
public static partial class HeldModelPlacement
{
    public struct Profile
    {
        public Vector3 handle, forward, up;
        public float length;
        public Profile(Vector3 h, Vector3 f, Vector3 u, float l)
        { handle=h;forward=f;up=u;length=l; }
    }

    public static bool TryGet(string modelName, out Profile p)
    {
        Vector3 x=Vector3.right,y=Vector3.up,z=Vector3.forward;
        p = modelName switch
        {
            "machete" => new Profile(new Vector3(0,.37f,0),-y,z,.85f),
            "ScrapBlade" => new Profile(new Vector3(0,.34f,0),-y,z,.65f),
            "SteelBlade" => new Profile(new Vector3(0,.36f,0),-y,z,.9f),
            "PipeMelee" => new Profile(new Vector3(0,-.33f,0),y,z,.7f),
            "EnergyInfusedBlade" => new Profile(new Vector3(-.36f,0,0),x,y,.95f),
            "VibroBlade" => new Profile(new Vector3(0,-.34f,0),y,x,.95f),
            "MyomerBlade" => new Profile(new Vector3(0,-.34f,0),y,x,1f),
            "AscendantWarblade" => new Profile(new Vector3(-.36f,0,0),x,z,1.05f),
            "pisol_pipe" => new Profile(new Vector3(-.3f,-.25f,0),x,y,.32f),
            "MidTierRifle" => new Profile(new Vector3(0,-.15f,.17f),-z,y,.95f),
            "AlloyRifle" => new Profile(new Vector3(-.18f,0,-.1f),x,z,.95f),
            "PrecisionRifle" => new Profile(new Vector3(-.17f,-.18f,0),x,y,1.05f),
            "ExperimentalEnergyRifle" => new Profile(new Vector3(-.2f,-.06f,0),x,y,.95f),
            "AscendantRailgun" => new Profile(new Vector3(.2f,-.12f,0),-x,y,1.1f),
            "pickaxe" => new Profile(new Vector3(0,-.2f,0),y,z,.85f),
            "hatchet" => new Profile(new Vector3(-.16f,-.25f,0),y,z,.65f),
            "FishingRod" => new Profile(new Vector3(0,-.34f,0),y,z,1.8f),
            "GeigerCounter" => new Profile(new Vector3(.43f,-.05f,0),y,z,.3f),
            "shield_scrap" => new Profile(new Vector3(0,0,-.3f),z,y,.65f),
            "PTShield01" => new Profile(new Vector3(0,0,-.34f),z,y,.55f),
            "PTShield06" => new Profile(new Vector3(0,0,-.34f),z,y,.6f),
            "PTShield19" => new Profile(new Vector3(.2f,.15f,-.3f),z,y,.85f),
            "GauntletRight" => new Profile(new Vector3(0,0,-.15f),z,y,.26f),
            "GauntletLeft" => new Profile(new Vector3(0,0,-.15f),z,y,.26f),
            _ => Generated(modelName)   // measured tools/gauntlets: HeldModelPlacement.Generated.cs
        };
        return p.length>0;
    }

    // The Synty hand's local -Y exits through the thumb; local +/-X runs down its fingers.
    // Use the same handle frame for every model instead of assuming every import faces +Z.
    public static Quaternion Rotation(Profile p, ItemData item, bool left)
    {
        Vector3 direction=Vector3.down,up=Vector3.back;
        if(item.weaponStyle==WeaponStyle.Fission)
        {direction=left?Vector3.left:Vector3.right;up=Vector3.down;}
        else if(item.type==ItemType.Shield)
        {direction=Vector3.up;up=Vector3.left;}
        return Quaternion.LookRotation(direction,up)*Quaternion.Inverse(Quaternion.LookRotation(p.forward,p.up));
    }
}
