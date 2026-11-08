/*
 * PRESENTATION ONLY: GameManager owns challenge timing and fingerprint reads.
 * Show(crew,seconds): top-screen prompt and lower-screen countdown.
 * SetTime(): slider and remaining time, cyan -> amber -> red.
 * ShowWrongFinger(): brief red screen flash + explicit wrong-finger message.
 * ShowResult(): success/timeout feedback. Hide(): hide this overlay.
 * References are assigned to the existing scene UI; no UI is rebuilt at runtime.
 */
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class CrewTakeoverUI : MonoBehaviour
{
    public TMP_Text crewPrompt;
    public TMP_Text instructions;
    public TMP_Text feedback;
    public TMP_Text timerText;
    public Slider timerSlider;
    public Image timerFill;
    public Image wrongScreenFlash;
    private float wrongUntil;
    private GameObject taskPreviewPanel;
    private TMP_Text taskPreview;
    private readonly Color cyan = new Color(0.15f, 0.85f, 1f);
    private readonly Color amber = new Color(1f, 0.68f, 0.1f);
    private readonly Color red = new Color(1f, 0.15f, 0.22f);

    public void Show(int crew, float duration, string preview = null)
    {
        gameObject.SetActive(true);
        wrongUntil = 0;
        ShowTaskPreview(preview ?? "SCAN TO TAKE CONTROL\nAuthenticate your crew to restore ship systems.");
        if (crewPrompt != null)
        {
            crewPrompt.gameObject.SetActive(true);
            crewPrompt.text = $"{CrewIdentity.Label(crew)}  //  TAKE CONTROL";
            crewPrompt.color = Color.white;
        }
        if (instructions != null) instructions.text = "SCAN YOUR FINGER on the fingerprint sensor";
        if (feedback != null) feedback.text = "";
        if (wrongScreenFlash != null) wrongScreenFlash.color = Color.clear;
        SetTime(duration, duration);
    }

    private void ShowTaskPreview(string preview)
    {
        if(taskPreviewPanel==null)
        {
            taskPreviewPanel=new GameObject("Upcoming Task Preview",typeof(RectTransform),typeof(UnityEngine.UI.Image));
            taskPreviewPanel.transform.SetParent(transform,false);
            var rect=taskPreviewPanel.GetComponent<RectTransform>();
            rect.anchorMin=rect.anchorMax=new Vector2(.5f,.5f);rect.sizeDelta=new Vector2(1120,280);
            var background=taskPreviewPanel.GetComponent<UnityEngine.UI.Image>();background.color=new Color(.015f,.035f,.065f,.94f);background.raycastTarget=false;
            var label=new GameObject("Upcoming Task Instructions",typeof(RectTransform),typeof(TextMeshProUGUI));label.transform.SetParent(taskPreviewPanel.transform,false);
            var textRect=label.GetComponent<RectTransform>();textRect.anchorMin=Vector2.zero;textRect.anchorMax=Vector2.one;textRect.offsetMin=new Vector2(35,25);textRect.offsetMax=new Vector2(-35,-25);
            taskPreview=label.GetComponent<TextMeshProUGUI>();taskPreview.font=crewPrompt!=null?crewPrompt.font:TMP_Settings.defaultFontAsset;
            taskPreview.fontSize=40;taskPreview.enableAutoSizing=true;taskPreview.fontSizeMin=28;taskPreview.fontSizeMax=40;
            taskPreview.alignment=TextAlignmentOptions.Center;taskPreview.color=Color.white;taskPreview.raycastTarget=false;
        }
        taskPreviewPanel.SetActive(true);
        int lineBreak=preview.IndexOf('\n');
        taskPreview.text=lineBreak<0?preview:$"<color=#44DDFF><size=135%>{preview.Substring(0,lineBreak)}</size></color>\n\n{preview.Substring(lineBreak+1)}";
    }

    public void SetTime(float remaining, float duration)
    {
        float fraction = duration > 0 ? Mathf.Clamp01(remaining / duration) : 0;
        if (timerSlider != null) timerSlider.value = fraction;
        Color colour = fraction > 0.5f ? Color.Lerp(amber, cyan, (fraction - 0.5f) * 2f)
            : Color.Lerp(red, amber, fraction * 2f);
        if (timerFill != null) timerFill.color = colour;
        if (timerText != null) { timerText.text = $"TAKEOVER WINDOW  /  {remaining:0.0}s"; timerText.color = colour; }
        if (Time.unscaledTime >= wrongUntil)
        {
            if (wrongScreenFlash != null) wrongScreenFlash.color = Color.clear;
            if (feedback != null) feedback.text = "";
        }
    }

    public void ShowWrongFinger()
    {
        wrongUntil = Time.unscaledTime + 0.8f;
        if (wrongScreenFlash != null) wrongScreenFlash.color = new Color(1, 0.05f, 0.1f, 0.22f);
        if (feedback != null) { feedback.text = "ACCESS DENIED — WRONG FINGER. TRY AGAIN."; feedback.color = red; }
    }

    public void ShowResult(int crew, bool succeeded)
    {
        if(taskPreviewPanel!=null)taskPreviewPanel.SetActive(false);
        if (!succeeded) SetTime(0, 1);
        if (crewPrompt != null)
        {
            crewPrompt.text = succeeded ? $"{CrewIdentity.Label(crew)}  //  CONTROL GRANTED" : "TAKEOVER WINDOW EXPIRED";
            crewPrompt.color = succeeded ? Color.white : red;
        }
        if (instructions != null) instructions.text = succeeded ? "Identity confirmed" : "Crew authentication failed";
        if (feedback != null) feedback.text = "";
        if (wrongScreenFlash != null) wrongScreenFlash.color = succeeded ? Color.clear : new Color(1, 0.05f, 0.1f, 0.18f);
    }

    public void ShowTaskFailure(int hearts,int maximum)
    {
        if(taskPreviewPanel!=null)taskPreviewPanel.SetActive(false);
        gameObject.SetActive(true);
        if(crewPrompt!=null){crewPrompt.text="HULL HIT — REPAIR FAILED";crewPrompt.color=red;}
        if(instructions!=null)instructions.text=$"{hearts} OF {maximum} HEARTS LEFT · Returning to normal space";
        if(feedback!=null)feedback.text="";
        if(timerSlider!=null)timerSlider.value=0;
        if(timerText!=null)timerText.text="CONTINUING MISSION";
    }

    public void Hide() { gameObject.SetActive(false); }
}
