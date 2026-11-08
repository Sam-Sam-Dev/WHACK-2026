using UnityEngine;

/// <summary>Stable crew colours shared by setup, takeovers and tasks. No scene
/// references or PlayerPrefs required; the same crew ID always has the same colour.</summary>
public static class CrewIdentity
{
    private static readonly Color[] Palette = {
        new Color(.2f,.85f,1f), new Color(1f,.68f,.2f),
        new Color(.8f,.5f,1f), new Color(.35f,1f,.65f),
        new Color(1f,.5f,.8f), new Color(.8f,1f,.3f),
        new Color(.55f,.7f,1f), new Color(1f,.85f,.55f)
    };
    public static Color GetColor(int crewId) => crewId > 0 ? Palette[(crewId-1)%Palette.Length] : Color.white;
    public static string Colorize(int crewId,string text) => $"<color=#{ColorUtility.ToHtmlStringRGB(GetColor(crewId))}>{text}</color>";
    public static string Label(int crewId) => Colorize(crewId,$"CREW #{crewId:00}");
}
