using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Faint ambient console messages, only during flight. Assign Telemetry
/// audio prefab in ShipSoundEvents for morse/radio feedback. No artwork required.</summary>
public sealed class ShipStatusFeed : MonoBehaviour
{
    public GameManager game;
    public Vector2 interval=new Vector2(3,6);
    [Min(1f)] public float charactersPerSecond=35f;
    [TextArea] public string[] messages={
        "NAV // Destination vector confirmed", "HULL // Integrity sensors responding",
        "COMMS // Listening for home beacon...", "ENGINE // Gravity pads stable",
        "SCAN // Uncharted bodies on long-range sensors", "NAV // Course correction queued",
        "REACTOR // Output within nominal limits", "COMMS // Parallel signal interference detected",
        "LIFE SUPPORT // Crew signatures present", "DESTINATION // Approach corridor calculated",
        "SENSORS // Stellar drift compensated", "FLIGHT // Wormhole residue decaying"};
    private TMP_Text[] labels;
    private float[] expires;
    private float[] spawnedAt;
    private float next;
    private int index;
    private void Start(){Build();next=Time.unscaledTime+2;}
    private void Build(){
        var go=new GameObject("Ambient Ship Console",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler));go.transform.SetParent(transform,false);
        var c=go.GetComponent<Canvas>();c.renderMode=RenderMode.ScreenSpaceOverlay;c.sortingOrder=5;
        var scaler=go.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1920,1080);
        labels=new TMP_Text[3];expires=new float[3];spawnedAt=new float[3];
        for(int i=0;i<3;i++){var label=new GameObject("Status "+i,typeof(RectTransform),typeof(TextMeshProUGUI));label.transform.SetParent(go.transform,false);var r=label.GetComponent<RectTransform>();r.sizeDelta=new Vector2(560,80);var text=label.GetComponent<TextMeshProUGUI>();text.font=TMP_Settings.defaultFontAsset;text.fontSize=19;text.raycastTarget=false;labels[i]=text;}
    }
    private void Update(){
        if(labels==null)return;
        bool flight=game!=null&&!game.IsGameOver&&!game.HasWon&&game.spaceMovie!=null&&game.spaceMovie.MovementEnabled;
        for(int i=0;i<3;i++){
            labels[i].color=new Color(.35f,.8f,.9f,flight?Mathf.Clamp01((expires[i]-Time.unscaledTime)/1.5f)*.38f:0);
            labels[i].maxVisibleCharacters=Mathf.FloorToInt(Mathf.Max(0,Time.unscaledTime-spawnedAt[i])*Mathf.Max(1f,charactersPerSecond));
        }
        if(!flight||Time.unscaledTime<next||messages==null||messages.Length==0)return;
        int slot=index++%3;labels[slot].text=messages[Random.Range(0,messages.Length)];
        labels[slot].maxVisibleCharacters=0;
        labels[slot].ForceMeshUpdate();
        spawnedAt[slot]=Time.unscaledTime;
        labels[slot].rectTransform.anchoredPosition=new Vector2(Random.value<.5f?-620:620,Random.Range(-280f,330f));expires[slot]=Time.unscaledTime+Mathf.Max(4f,labels[slot].textInfo.characterCount/Mathf.Max(1f,charactersPerSecond)+2f);
        next=Time.unscaledTime+Random.Range(Mathf.Max(1,interval.x),Mathf.Max(1,interval.y));game.PlaySound(ShipSoundEvents.Cue.Telemetry);
    }
}
