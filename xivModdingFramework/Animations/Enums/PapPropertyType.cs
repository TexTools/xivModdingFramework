namespace xivModdingFramework.Animations.Enums
{
    public enum PapPropertyType
    {
        TMDH = 0x544D4448, // TM (DH? Data Header)
        TMAL = 0x544D414C, // TM (AL? Animation Length)
        TMAC = 0x544D4143, // TM (AC? Animation Count)
        TMTR = 0x544D5452, // TM (TR?)
        C009 = 0x43303039, // Begin?
        C010 = 0x43303130, // Face Animation
        C042 = 0x43303432, // End?
    }
}