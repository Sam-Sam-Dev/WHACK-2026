using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Persistent three-strike HUD. Assign Heart Sprite later; plain red squares
/// are intentional placeholders. Sorting keeps health visible above task textures.</summary>
public sealed class ShipHealthUI : MonoBehaviour
{
    public Sprite heartSprite;
    private Image[] hearts;
    private TMP_Text gameOver;
    public void SetHealth(int count,int maximum=3)
    {
        Build(maximum);for(int i=0;i<hearts.Length;i++){hearts[i].sprite=heartSprite;hearts[i].color=i<count?new Color(1,.15f,.25f):new Color(.2f,.2f,.25f,.6f);}
    }
    private void Build(int maximum=3)
    {
        if(hearts!=null)return;
        var go=new GameObject("Ship Health HUD",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler));go.transform.SetParent(transform,false);
        var canvas=go.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceOverlay;canvas.sortingOrder=200;
        var scaler=go.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1920,1080);
        hearts=new Image[Mathf.Clamp(maximum,1,20)];
        for(int i=0;i<hearts.Length;i++){var icon=new GameObject("Heart "+(i+1),typeof(RectTransform),typeof(Image));icon.transform.SetParent(go.transform,false);var r=icon.GetComponent<RectTransform>();r.anchorMin=r.anchorMax=new Vector2(0,1);r.anchoredPosition=new Vector2(64+i*80,-64);r.sizeDelta=new Vector2(64,64);hearts[i]=icon.GetComponent<Image>();hearts[i].raycastTarget=false;}
        var text=new GameObject("Ship Destroyed",typeof(RectTransform),typeof(TextMeshProUGUI));text.transform.SetParent(go.transform,false);var rect=text.GetComponent<RectTransform>();rect.sizeDelta=new Vector2(1500,180);
        gameOver=text.GetComponent<TextMeshProUGUI>();gameOver.font=TMP_Settings.defaultFontAsset;gameOver.fontSize=75;gameOver.alignment=TextAlignmentOptions.Center;gameOver.color=new Color(1,.15f,.25f);gameOver.raycastTarget=false;gameOver.text="";
    }
    public void ShowVictory(){Build();gameOver.color=new Color(.3f,1,.6f);gameOver.text="DESTINATION REACHED\n<size=50%>MISSION COMPLETE</size>";}
    public void ShowGameOver(){Build();gameOver.text="SHIP DESTROYED";}
}
