using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Secondary orthographic camera renders all task UI to a texture.
/// Placeholder rectangles are intentional; sprite fields can be assigned later.</summary>
public sealed class MissionTaskDisplay : MonoBehaviour
{
    public Sprite shipSprite, asteroidSprite, ultrasonicGuide;
    public TMP_Text title, description, sequence, status, distanceReadout, timerLabel, crewBadge;
    public RectTransform field;
    public Image timerFill, holdFill, guide;
    public UnityEngine.UI.Image lowerDistanceBar, upperDistanceBar;
    public TMP_Text[] keys;
    private GameObject screen, stage;
    private Camera taskCamera;
    private RenderTexture texture;
    private Camera mainCamera;
    private int mainMask;
    public Color Good => new Color(.2f,1,.6f);
    public Color Bad => new Color(1,.18f,.25f);

    public void Build()
    {
        if (screen != null) return;
        texture = new RenderTexture(1000, 700, 24); texture.name = "Mission Task Render Texture";
        stage = new GameObject("Task Camera Stage"); stage.transform.SetParent(transform);
        stage.transform.position = new Vector3(10000,10000,10000);
        var cameraObject = new GameObject("Minigame Camera"); cameraObject.transform.SetParent(stage.transform,false);
        cameraObject.transform.localPosition = new Vector3(0,0,-10);
        taskCamera = cameraObject.AddComponent<Camera>(); taskCamera.orthographic=true;
        taskCamera.orthographicSize=3.5f; taskCamera.aspect=10f/7f;taskCamera.targetTexture=texture;
        taskCamera.cullingMask=1<<31;taskCamera.clearFlags=CameraClearFlags.SolidColor;
        taskCamera.backgroundColor=new Color(.012f,.025f,.05f);taskCamera.nearClipPlane=.1f;taskCamera.farClipPlane=20;
        mainCamera=Camera.main;if(mainCamera!=null){mainMask=mainCamera.cullingMask;mainCamera.cullingMask &= ~(1<<31);}
        var world = new GameObject("Task World Canvas",typeof(RectTransform),typeof(Canvas));
        world.transform.SetParent(stage.transform,false);world.layer=31;
        var canvas=world.GetComponent<Canvas>();canvas.renderMode=RenderMode.WorldSpace;canvas.worldCamera=taskCamera;
        var rect=world.GetComponent<RectTransform>();rect.sizeDelta=new Vector2(1000,700);rect.localScale=Vector3.one*.01f;
        crewBadge=Text("Crew Identity",rect,new Vector2(0,337),new Vector2(940,24),20);
        title=Text("Title",rect,new Vector2(0,300),new Vector2(960,60),38);
        description=Text("Instructions",rect,new Vector2(0,250),new Vector2(950,55),26);
        sequence=Text("Sequence",rect,new Vector2(0,190),new Vector2(950,60),42);
        field=Box("Play Field",rect,Vector2.zero,new Vector2(860,320),new Color(.025f,.055f,.1f)).rectTransform;
        field.gameObject.AddComponent<RectMask2D>(); // Rocks enter visibly through the top edge.
        keys=new TMP_Text[16];
        for(int y=0;y<4;y++)for(int x=0;x<4;x++){
            var key=Box("Key",field,new Vector2((x-1.5f)*100,(1.5f-y)*70),new Vector2(88,60),new Color(.08f,.16f,.23f));
            keys[y*4+x]=Text("Symbol",key.transform,Vector2.zero,new Vector2(80,55),36);
        }
        guide=Box("Ultrasonic Guide",field,new Vector2(-240,0),new Vector2(220,240),Color.white);guide.preserveAspect=true;
        lowerDistanceBar=Box("Lower Gravity Pad",field,new Vector2(250,-12),new Vector2(300,10),Color.gray);
        upperDistanceBar=Box("Upper Gravity Pad",field,new Vector2(250,12),new Vector2(300,10),Color.gray);
        distanceReadout=Text("Distance Readout",field,new Vector2(-15,0),new Vector2(240,180),54);
        guide.rectTransform.anchoredPosition=new Vector2(-280,0);guide.rectTransform.sizeDelta=new Vector2(280,210);
        status=Text("Status",rect,new Vector2(0,-220),new Vector2(940,55),27);
        var hold=Box("Distance Hold Background",rect,new Vector2(0,-265),new Vector2(850,10),new Color(.1f,.15f,.2f));
        holdFill=Box("Distance Hold",hold.transform,Vector2.zero,new Vector2(850,10),Good);
        timerLabel=Text("Seconds Left",rect,new Vector2(0,-289),new Vector2(900,28),22);
        var background=Box("Timer Background",rect,new Vector2(0,-322),new Vector2(900,16),new Color(.1f,.15f,.2f));
        timerFill=Box("Timer",background.transform,Vector2.zero,new Vector2(900,16),Color.cyan);
        screen=new GameObject("Minigame Screen",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler));screen.transform.SetParent(transform,false);
        var overlay=screen.GetComponent<Canvas>();overlay.renderMode=RenderMode.ScreenSpaceOverlay;overlay.sortingOrder=100;
        var scaler=screen.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1920,1080);
        Box("Dim Background",screen.transform,Vector2.zero,new Vector2(1920,1080),new Color(0,0,0,.8f));
        var output=new GameObject("Task Render Texture",typeof(RectTransform),typeof(RawImage));output.transform.SetParent(screen.transform,false);
        var outputRect=output.GetComponent<RectTransform>();outputRect.anchorMin=outputRect.anchorMax=new Vector2(.5f,.5f);outputRect.sizeDelta=new Vector2(1300,910);
        var raw=output.GetComponent<RawImage>();raw.texture=texture;raw.raycastTarget=false;
        Hide();
    }
    public Image Box(string name,Transform parent,Vector2 position,Vector2 size,Color colour,Sprite sprite=null)
    {
        var go=new GameObject(name,typeof(RectTransform),typeof(Image));go.layer=31;go.transform.SetParent(parent,false);
        var rect=go.GetComponent<RectTransform>();rect.anchorMin=rect.anchorMax=new Vector2(.5f,.5f);rect.anchoredPosition=position;rect.sizeDelta=size;
        var image=go.GetComponent<Image>();image.color=colour;image.sprite=sprite;image.raycastTarget=false;return image;
    }
    private TMP_Text Text(string name,Transform parent,Vector2 position,Vector2 size,int fontSize)
    {
        var go=new GameObject(name,typeof(RectTransform),typeof(TextMeshProUGUI));go.layer=31;go.transform.SetParent(parent,false);
        var rect=go.GetComponent<RectTransform>();rect.anchoredPosition=position;rect.sizeDelta=size;
        var text=go.GetComponent<TextMeshProUGUI>();text.font=TMP_Settings.defaultFontAsset;text.fontSize=fontSize;text.enableAutoSizing=true;text.fontSizeMin=18;text.fontSizeMax=fontSize;text.alignment=TextAlignmentOptions.Center;text.color=Color.white;text.raycastTarget=false;return text;
    }
    public void Show(int mode)
    {
        Build();screen.SetActive(true);stage.SetActive(true);title.color=Color.white;status.color=Color.white;status.text="";
        sequence.text="";foreach(var key in keys)key.transform.parent.gameObject.SetActive(mode==1);
        lowerDistanceBar.gameObject.SetActive(mode==3);upperDistanceBar.gameObject.SetActive(mode==3);
        SetDistanceBars(0,new Vector2(15,35),false,false);
        distanceReadout.gameObject.SetActive(mode==3);distanceReadout.text="— cm";
        guide.gameObject.SetActive(mode==3 && ultrasonicGuide!=null);guide.sprite=ultrasonicGuide;
        holdFill.transform.parent.gameObject.SetActive(mode==3);SetBar(holdFill,0,Good);
    }
    public void SetDistanceBars(float distance,Vector2 range,bool fresh,bool perfect)
    {
        if(lowerDistanceBar==null || upperDistanceBar==null)return;
        float halfGap=Mathf.Lerp(12f,120f,fresh?Mathf.InverseLerp(range.x,range.y,distance):0f);
        lowerDistanceBar.rectTransform.anchoredPosition=new Vector2(250,-halfGap);
        upperDistanceBar.rectTransform.anchoredPosition=new Vector2(250,halfGap);
        Color colour=!fresh?Color.gray:perfect?Good:Bad;
        lowerDistanceBar.color=upperDistanceBar.color=colour;
    }
    public void SetCrew(int crew){if(crewBadge!=null){crewBadge.text=CrewIdentity.Label(crew)+"  //  YOUR REALM";crewBadge.color=Color.white;}}
    public void SetTimer(float remaining,float duration){if(timerLabel!=null)timerLabel.text=$"TIME LEFT  {Mathf.Max(0,remaining):0.0}s";float t=Mathf.Clamp01(remaining/duration);SetBar(timerFill,t,Color.Lerp(Bad,Color.cyan,t));}
    public void SetBar(Image image,float fraction,Color colour){image.rectTransform.pivot=new Vector2(0,.5f);image.rectTransform.anchorMin=image.rectTransform.anchorMax=new Vector2(0,.5f);image.rectTransform.anchoredPosition=Vector2.zero;image.rectTransform.localScale=new Vector3(Mathf.Clamp01(fraction),1,1);image.color=colour;}
    public void Hide(){if(screen!=null)screen.SetActive(false);if(stage!=null)stage.SetActive(false);}
    private void OnDestroy(){if(mainCamera!=null)mainCamera.cullingMask=mainMask;if(texture!=null){texture.Release();Destroy(texture);}}
}
