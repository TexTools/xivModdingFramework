namespace xivModdingFramework.Animations.Enums
{
    public enum PapPropertyType
    {
        TMDH = 0x48444D54, // TM (DH? Data Header)
        TMAL = 0x4C414D54, // TM (AL? Animation Length)
        TMAC = 0x43414D54, // TM (AC? Animation Count)
        TMTR = 0x52544D54, // TM (TR?)
        C009 = 0x39303043, // Begin?
        C010 = 0x30313043, // Face Animation
        C042 = 0x32343043, // End?
    }
}