using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Hardware-only task input. Combined game odds ramp from 70% to 90%
/// with route progress; the three minigames share those odds equally.
/// Deadlines start after peripheral activation; missing hardware fails after 8s.
/// Task callbacks are released before completion. No physical collision components needed:
/// asteroid AABB collisions are evaluated in the secondary camera's 2D play field.</summary>
public sealed class MissionTaskController : MonoBehaviour
{
    public MissionTaskDisplay display;
    [Header("Outcome odds (0–1; remaining chance is fingerprint-only)")]
    [Range(0,1)] public float minigameChanceAtStart = .7f;
    [Range(0,1)] public float minigameChanceAtEnd = .9f;
    [Tooltip("Relative keypad / joystick / ultrasonic weights. Equal values give equal odds.")]
    public Vector3 minigameWeights = Vector3.one;
    [Header("Shared timing")]
    [Tooltip("Read the task instructions before the countdown/input scoring begins.")]
    [Min(0)] public float instructionSeconds = 2f;
    private float instructionsUntil;
    [Min(.1f)] public float activationTimeout = 8f;
    [Min(0)] public float normalRecoverySeconds = 1f;
    [Min(0)] public float resultDisplaySeconds = 1.4f;
    [Min(0)] public float timerWarningSeconds = 2f;
    [Header("Keypad difficulty")]
    public Vector2Int sequenceLengthRange = new Vector2Int(8,12);
    [Min(.05f)] public float secondsPerCharacter = 1.5f;
    [Min(1)] public int keypadMistakesToFail = 2;
    [Header("Asteroid difficulty (playfield UI units)")]
    [Tooltip("Log raw readings and the selected crew axis while debugging joystick direction.")]
    public bool logJoystickInput = false;
    private float nextJoystickLog;
    public Vector2 asteroidDurationRange = new Vector2(7,9);
    [Min(0)] public float firstAsteroidDelay = .5f;
    [Min(.05f)] public float asteroidSpawnInterval = .35f;
    [UnityEngine.Serialization.FormerlySerializedAs("asteroidSpeed")]
    [Min(1)] public float asteroidSpeedAtStart = 190f;
    [Min(1)] public float asteroidSpeedAtEnd = 340f;
    private float currentAsteroidSpeed;
    [Min(1)] public float shipSpeed = 420f;
    public Vector2 shipSize = new Vector2(45,32);
    public Vector2 asteroidSize = new Vector2(42,42);
    public Vector2 AsteroidCollisionSize => Vector2.one * (Mathf.Min(asteroidSize.x, asteroidSize.y) * .75f);
    [Min(0)] public float playfieldMargin = 8f;
    [Min(0)] public float shipBottomMargin = 19f;
    [Min(1)] public int asteroidHitsToFail = 2;
    [Min(0)] public float collisionGraceSeconds = .55f;
    [Header("Ultrasonic calibration")]
    public Vector2 targetDistanceRangeCm = new Vector2(15,35);
    [Min(0)] public float distanceToleranceCm = 3f;
    [Min(.1f)] public float ultrasonicSeconds = 8f;
    [Min(.05f)] public float requiredHoldSeconds = 1f;
    [Min(.05f)] public float readingFreshnessSeconds = .3f;

    private void OnValidate() => ValidateSettings();
    private void ValidateSettings()
    {
        minigameChanceAtStart=Mathf.Clamp01(minigameChanceAtStart);minigameChanceAtEnd=Mathf.Clamp01(minigameChanceAtEnd);
        minigameWeights=Vector3.Max(Vector3.zero,minigameWeights);
        instructionSeconds=Mathf.Max(0,instructionSeconds);activationTimeout=Mathf.Max(.1f,activationTimeout);normalRecoverySeconds=Mathf.Max(0,normalRecoverySeconds);resultDisplaySeconds=Mathf.Max(0,resultDisplaySeconds);timerWarningSeconds=Mathf.Max(0,timerWarningSeconds);
        sequenceLengthRange.x=Mathf.Clamp(sequenceLengthRange.x,1,64);sequenceLengthRange.y=Mathf.Clamp(sequenceLengthRange.y,sequenceLengthRange.x,64);
        secondsPerCharacter=Mathf.Max(.05f,secondsPerCharacter);keypadMistakesToFail=Mathf.Max(1,keypadMistakesToFail);
        asteroidDurationRange.x=Mathf.Max(.1f,asteroidDurationRange.x);asteroidDurationRange.y=Mathf.Max(asteroidDurationRange.x,asteroidDurationRange.y);
        firstAsteroidDelay=Mathf.Max(0,firstAsteroidDelay);asteroidSpawnInterval=Mathf.Max(.05f,asteroidSpawnInterval);asteroidSpeedAtStart=Mathf.Max(1,asteroidSpeedAtStart);asteroidSpeedAtEnd=Mathf.Max(asteroidSpeedAtStart,asteroidSpeedAtEnd);shipSpeed=Mathf.Max(1,shipSpeed);
        shipSize=Vector2.Max(Vector2.one,shipSize);asteroidSize=Vector2.Max(Vector2.one,asteroidSize);playfieldMargin=Mathf.Max(0,playfieldMargin);shipBottomMargin=Mathf.Max(0,shipBottomMargin);
        asteroidHitsToFail=Mathf.Max(1,asteroidHitsToFail);collisionGraceSeconds=Mathf.Max(0,collisionGraceSeconds);
        targetDistanceRangeCm.x=Mathf.Max(0,targetDistanceRangeCm.x);targetDistanceRangeCm.y=Mathf.Max(targetDistanceRangeCm.x,targetDistanceRangeCm.y);
        distanceToleranceCm=Mathf.Max(0,distanceToleranceCm);ultrasonicSeconds=Mathf.Max(.1f,ultrasonicSeconds);requiredHoldSeconds=Mathf.Max(.05f,requiredHoldSeconds);readingFreshnessSeconds=Mathf.Max(.05f,readingFreshnessSeconds);
    }
    public int ChooseTask(float progress)
    {
        ValidateSettings();float total=minigameWeights.x+minigameWeights.y+minigameWeights.z;
        if(total<=0 || UnityEngine.Random.value>=GetMinigameChance(progress,minigameChanceAtStart,minigameChanceAtEnd))return 0;
        float roll=UnityEngine.Random.Range(0,total);return roll<minigameWeights.x?1:roll<minigameWeights.x+minigameWeights.y?2:3;
    }
    private GameManager game;
    private ComponentManager input;
    private Action<bool> completed;
    private int mode,crew,index,mistakes;
    private bool active,ready,resultVisible,warned;
    private float resultUntil;
    private float deadline,duration,activationDeadline,lastSample,hold,target,axis,nextAsteroid,invulnerable;
    private string code;
    private readonly List<bool> results=new List<bool>();
    private readonly List<RectTransform> asteroids=new List<RectTransform>();
    private RectTransform ship;
    public bool IsRunning=>active;
    public void StartRandom(GameManager manager,int crewId,Action<bool> callback)
    {
        float progress=manager.spaceMovie!=null && manager.spaceMovie.GameLength>0
            ? manager.spaceMovie.pastTime/manager.spaceMovie.GameLength : 0;
        int selected=ChooseTask(progress);
        StartTask(selected,manager,crewId,callback);
    }
    public static string GetTaskPreview(int task)
    {
        switch(task)
        {
            case 1:return "KEYPAD // ENTER THE CODE\nRead your crew's keypad layout and enter the displayed sequence.";
            case 2:return "JOYSTICK // DODGE ASTEROIDS\nMove left and right to dodge incoming asteroids until time runs out.";
            case 3:return "GRAVITY PADS // CALIBRATE ENGINE\nRaise or lower your hand above the sensor. Hold the pads GREEN at the target distance.";
            default:return "RESTORE SHIP SYSTEMS\nScan your fingerprint to stabilise the ship. No manual repair required.";
        }
    }
    public static float GetAsteroidSpeed(float progress,float start,float end)
    {
        start=Mathf.Max(1f,start);end=Mathf.Max(start,end);
        float t=float.IsNaN(progress)||float.IsInfinity(progress)?0f:Mathf.Clamp01(progress);
        return Mathf.Lerp(start,end,t);
    }
    public static float GetMinigameChance(float progress, float start=.7f, float end=.9f) => Mathf.Lerp(Mathf.Clamp01(start),Mathf.Clamp01(end),
        float.IsNaN(progress)||float.IsInfinity(progress)?0:Mathf.Clamp01(progress));
    public void StartTask(int task,GameManager manager,int crewId,Action<bool> callback)
    {
        if(active)return;
        ValidateSettings();
        game=manager;input=manager.components;crew=crewId;completed=callback;mode=task;warned=false;game.PlaySound(ShipSoundEvents.Cue.TaskStarted);
        if(mode==0){StartCoroutine(NormalRecovery());return;}
        if(display==null)display=GetComponent<MissionTaskDisplay>();
        if(display==null)display=gameObject.AddComponent<MissionTaskDisplay>();
        instructionsUntil=0;resultVisible=false;activationSent=false;display.Show(mode);display.SetCrew(crew);active=true;ready=false;index=mistakes=0;axis=hold=0;lastSample=float.NegativeInfinity;results.Clear();
        activationDeadline=Time.unscaledTime+activationTimeout;display.status.text="Connecting to control panel...";
        if(mode==1){int length=UnityEngine.Random.Range(sequenceLengthRange.x,sequenceLengthRange.y+1);duration=length*secondsPerCharacter;var builder=new StringBuilder();for(int i=0;i<length;i++)builder.Append("0123456789ABCDEF"[UnityEngine.Random.Range(0,16)]);code=builder.ToString();
            display.title.text="KEYPAD // ENTER THE CODE";display.description.text=$"Press the physical keypad in this order · {keypadMistakesToFail} mistakes = failure";
            for(int y=0;y<4;y++)for(int x=0;x<4;x++){game.TryGetCrewKeypadSymbol(crew,new Vector2Int(x,y),out char c);display.keys[y*4+x].text=c.ToString();}
            LogKeypadLayout();DrawSequence();}
        else if(mode==2){
            float progress=manager.spaceMovie!=null && manager.spaceMovie.GameLength>0 ? manager.spaceMovie.pastTime/manager.spaceMovie.GameLength : 0f;
            currentAsteroidSpeed=GetAsteroidSpeed(progress,asteroidSpeedAtStart,asteroidSpeedAtEnd);
            duration=UnityEngine.Random.Range(asteroidDurationRange.x,asteroidDurationRange.y);display.title.text="JOYSTICK // DODGE ASTEROIDS";display.description.text=$"Move your ship left / right · survive the timer · {asteroidHitsToFail} hits = failure";
            ship=display.Box("Mini Ship",display.field,new Vector2(0,display.field.rect.yMin+shipSize.y*.5f+shipBottomMargin),shipSize,Color.cyan,display.shipSprite).rectTransform;nextAsteroid=float.PositiveInfinity;invulnerable=0;}
        else {duration=ultrasonicSeconds;target=UnityEngine.Random.Range(targetDistanceRangeCm.x,targetDistanceRangeCm.y);display.title.text="HAND HEIGHT // CALIBRATE ENGINE";display.description.text=$"Move your hand above the sensor · hold GREEN for {requiredHoldSeconds:0.#} second(s)";}
        if(mode==3)display.sequence.text=$"TARGET {target:0.0} cm  |  GREEN ZONE {Mathf.Max(0,target-distanceToleranceCm):0.0}–{target+distanceToleranceCm:0.0} cm";
        if(mode==2){var rules=game.GetCrewInputCorruption(crew);display.sequence.text="USE LEFT / RIGHT · "+(rules!=null && rules.InvertJoystick?"INVERTED":"NORMAL DIRECTION");}
        display.SetTimer(duration,duration);
    }
    private System.Collections.IEnumerator NormalRecovery()
    {
        active=true;
        if(display==null)display=GetComponent<MissionTaskDisplay>();
        if(display!=null){display.Show(0);display.SetCrew(crew);display.title.text="STABILISING SHIP SYSTEMS";display.description.text="No manual repair required";}
        yield return new WaitForSecondsRealtime(normalRecoverySeconds);
        if(!active)yield break;
        Finish(true);
    }
    private void Update()
    {
        if(!active){if(resultVisible&&Time.unscaledTime>=resultUntil){resultVisible=false;display.Hide();}return;}
        if(mode==0)return;
        if(!ready){
            if(Time.unscaledTime>=activationDeadline){Finish(false);return;}
            if(input==null)return;
            var desired=mode==1?PanelSerialProtocol.Component.Keypad:mode==2?PanelSerialProtocol.Component.Joystick:PanelSerialProtocol.Component.Ultrasonic;
            bool connected=mode==1?input.IsFingerprintConnectionReady:input.IsUltrasonicJoystickConnectionReady;
            if(!connected)return;
            var current=mode==1?input.FingerprintKeypadActive:input.UltrasonicJoystickActive;
            if(current!=desired){if(!activationSent){activationSent=true;if(mode==1)input.ActivateKeypad();else if(mode==2)input.ActivateJoystick();else input.ActivateUltrasonic();}return;}
            ready=true;instructionsUntil=Time.unscaledTime+instructionSeconds;deadline=instructionsUntil+duration;display.status.text="";
            if(mode==2)nextAsteroid=instructionsUntil+firstAsteroidDelay;
            if(mode==1)input.SetKeypadCallback(Key);
            else if(mode==2)input.SetJoystickCallback(Joystick);
            else input.SetUltrasonicCallback(value=>{height=value;lastSample=Time.unscaledTime;});
        }
        if(Time.unscaledTime<instructionsUntil){display.status.text=$"GET READY · {Mathf.CeilToInt(instructionsUntil-Time.unscaledTime)}";display.status.color=Color.cyan;display.SetTimer(duration,duration);return;}
        float remaining=Mathf.Max(0,deadline-Time.unscaledTime);display.SetTimer(remaining,duration);
        if(remaining>0 && remaining<=timerWarningSeconds && !warned){warned=true;game.PlaySound(ShipSoundEvents.Cue.TimerWarning);}
        if(remaining<=0){Finish(mode==2);return;}
        if(mode==2)UpdateAsteroids();
        if(mode==3){bool fresh=Time.unscaledTime-lastSample<readingFreshnessSeconds;bool perfect=fresh&&Mathf.Abs(height-target)<=distanceToleranceCm;
            display.SetDistanceBars(height,targetDistanceRangeCm,fresh,perfect);
            hold=perfect?hold+Time.unscaledDeltaTime:0;
            display.status.text=!fresh?"WAITING FOR HAND READING":perfect?$"PERFECT · hold {Mathf.Min(hold,requiredHoldSeconds):0.0}/{requiredHoldSeconds:0.0}s":height<target?"TOO LOW — raise your hand":"TOO HIGH — lower your hand";
            display.distanceReadout.text=fresh?$"{height:0.0} cm\n<size=45%>{(perfect?"PERFECT":height<target?"TOO LOW":"TOO HIGH")}</size>":"— cm\n<size=45%>NO READING</size>";
            display.distanceReadout.color=perfect?display.Good:display.Bad;
            display.status.color=perfect?display.Good:display.Bad;display.SetBar(display.holdFill,hold/requiredHoldSeconds,display.Good);
            if(hold>=requiredHoldSeconds){game.PlaySound(ShipSoundEvents.Cue.GravityPerfect);Finish(true);}}
    }
    private bool activationSent;
    private float height;
    private void Joystick(Vector2 value)
    {
        if(!active || !ready || mode!=2)return;
        Vector2 taskInput=value;
        var rules=game!=null?game.GetCrewInputCorruption(crew):null;
        if(rules!=null && rules.InvertJoystick)taskInput=-taskInput;
        axis=taskInput.x;
        lastSample=Time.unscaledTime;
        if(!logJoystickInput || Time.unscaledTime<nextJoystickLog)return;
        nextJoystickLog=Time.unscaledTime+.5f;
        Debug.Log($"JOYSTICK crew={crew}: raw X={input.RawJoystick.x} Y={input.RawJoystick.y}; normalized X={value.x:F3} Y={value.y:F3}; selected=X; movement={axis:F3}",this);
    }
    private void Key(Vector2Int position)
    {
        if(!active||!ready||mode!=1||Time.unscaledTime<instructionsUntil||Time.unscaledTime>=deadline)return;
        // The physical keypad reports the two axes transposed relative to the displayed grid.
        // Swap ONCE here, before BOTH the character lookup and validation/logging.
        position = new Vector2Int(position.y, position.x);
        if(!game.TryGetCrewKeypadSymbol(crew,position,out char symbol))return;
        // The corrected position uses X = column and Y = row.
        var crewLayout=game.GetCrewInputCorruption(crew)?.KeypadLayout;
        if(string.IsNullOrEmpty(crewLayout))return;
        char expectedCharacter=code[index];
        int expectedIndex=crewLayout.IndexOf(expectedCharacter);
        if(expectedIndex<0)return;
        int expectedColumn=expectedIndex%4;
        int expectedRow=expectedIndex/4;
        bool correct=position.x==expectedColumn && position.y==expectedRow;
        Debug.Log($"KEYPAD VALIDATE crew={crew}: x/column={position.x}, y/row={position.y}, character='{symbol}'; expected '{expectedCharacter}' at x/column={expectedColumn}, y/row={expectedRow}; correct={correct}",this);
        results.Add(correct);index++;
        if(!correct)
        {
            char expected=code[index-1];
            var layout=game.GetCrewInputCorruption(crew)?.KeypadLayout;
            int location=string.IsNullOrEmpty(layout)?-1:layout.IndexOf(expected);
            if(location>=0)
                Debug.Log($"KEYPAD WRONG crew={crew}: pressed '{symbol}' at (row={position.y}, column={position.x}); expected '{expected}' at (x={location%4}, y={location/4}; row={location/4}, column={location%4}). Coordinates are zero-based, top-left=(0,0).",this);
        }
        if(correct)game.PlaySound(ShipSoundEvents.Cue.KeyCorrect);
        if(!correct){game.PlaySound(ShipSoundEvents.Cue.KeyWrong);mistakes++;game.PlayTaskErrorAudio();display.status.text=$"WRONG · {mistakes}/{keypadMistakesToFail} mistakes — next key";display.status.color=display.Bad;}
        else {display.status.text=$"CORRECT · {index}/{code.Length} entered";display.status.color=display.Good;}
        DrawSequence();if(mistakes>=keypadMistakesToFail)Finish(false);else if(index==code.Length)Finish(true);
    }
    private void DrawSequence(){var text=new StringBuilder();for(int i=0;i<code.Length;i++){string colour=i<results.Count?(results[i]?"#36FF99":"#FF3048"):(i==index?"#44DDFF":"#FFFFFF");text.Append($"<color={colour}>{code[i]}</color> ");}display.sequence.text=text.ToString();}
    private void LogKeypadLayout()
    {
        var layout=game.GetCrewInputCorruption(crew)?.KeypadLayout;
        if(string.IsNullOrEmpty(layout)||layout.Length!=16)return;
        var text=new StringBuilder($"KEYPAD crew={crew} corrupted layout — zero-based (row, column), top-left=(0,0):\n          col 0  1  2  3\n");
        for(int row=0;row<4;row++)
        {
            text.Append($"row {row}:    [ ");
            for(int col=0;col<4;col++)text.Append(layout[row*4+col]).Append(' ');
            text.AppendLine("]");
        }
        Debug.Log(text.ToString(),this);
    }
    public static Vector2 CalculateJoystickMovement(float axis,float speed,float step)
    {
        return new Vector2(axis*speed*step,0f);
    }

    private void UpdateAsteroids()
    {
        float step=Mathf.Min(Time.unscaledDeltaTime,.05f); // Avoid jumping down the field after a slow frame.
        Vector2 movement=Vector2.zero;
        if(input!=null && input.IsUltrasonicJoystickConnectionReady &&
            input.UltrasonicJoystickActive==PanelSerialProtocol.Component.Joystick &&
            Time.unscaledTime-lastSample<readingFreshnessSeconds)
        {
            movement=CalculateJoystickMovement(axis,shipSpeed,step);
        }
        var position=ship.anchoredPosition;
        position+=movement;
        position.x=Mathf.Clamp(position.x,display.field.rect.xMin+shipSize.x*.5f+playfieldMargin,display.field.rect.xMax-shipSize.x*.5f-playfieldMargin);
        ship.anchoredPosition=position;
        ship.GetComponent<Image>().color=Time.unscaledTime<invulnerable?display.Bad:Color.cyan;
        Vector2 collisionSize=AsteroidCollisionSize;
        for(int i=asteroids.Count-1;i>=0;i--){var rock=asteroids[i];rock.anchoredPosition+=Vector2.down*currentAsteroidSpeed*step;
            var delta=rock.anchoredPosition-ship.anchoredPosition;
            bool hit=Time.unscaledTime>=invulnerable&&Mathf.Abs(delta.x)<(shipSize.x+collisionSize.x)*.5f&&Mathf.Abs(delta.y)<(shipSize.y+collisionSize.y)*.5f;
            if(hit){game.PlaySound(ShipSoundEvents.Cue.AsteroidImpact);mistakes++;invulnerable=Time.unscaledTime+collisionGraceSeconds;game.PlayTaskErrorAudio();display.status.text=$"IMPACT · {mistakes}/{asteroidHitsToFail} hits";display.status.color=display.Bad;}
            if(hit||rock.anchoredPosition.y<display.field.rect.yMin-asteroidSize.y*.5f){RemovePiece(rock.gameObject);asteroids.RemoveAt(i);}
            if(mistakes>=asteroidHitsToFail){Finish(false);return;}}
        // Spawn AFTER updating existing rocks: the first rendered frame is always
        // above the top edge, never already partway down the field.
        if(Time.unscaledTime>=nextAsteroid){nextAsteroid=Time.unscaledTime+asteroidSpawnInterval;
            float top=display.field.rect.yMax+asteroidSize.y*.5f+2f;
            asteroids.Add(display.Box("Asteroid",display.field,new Vector2(UnityEngine.Random.Range(display.field.rect.xMin+asteroidSize.x*.5f+playfieldMargin,display.field.rect.xMax-asteroidSize.x*.5f-playfieldMargin),top),asteroidSize,new Color(.7f,.5f,.35f),display.asteroidSprite).rectTransform);}
    }
    private void Finish(bool success)
    {
        if(!active)return;active=false;ReleaseInput();ClearPieces();
        display.title.text=success?"SYSTEM RESTORED":"REPAIR FAILED";display.description.text=success?"Well done — continuing the journey":"Hull damaged — returning to normal space";display.title.color=success?display.Good:display.Bad;
        if(!success)game.PlayTaskErrorAudio();
        Debug.Log($"Crew #{crew} task {mode}: {(success?"success":"failure")}");
        var done=completed;completed=null;done?.Invoke(success);
        resultVisible=true;resultUntil=Time.unscaledTime+resultDisplaySeconds;
    }
    private void ReleaseInput(){activationSent=false;if(input==null)return;if(mode==1)input.DeactivateKeypad();else if(mode==2)input.DeactivateJoystick();else if(mode==3)input.DeactivateUltrasonic();}
    private void ClearPieces(){if(ship!=null)RemovePiece(ship.gameObject);ship=null;foreach(var rock in asteroids)if(rock!=null)RemovePiece(rock.gameObject);asteroids.Clear();}
    private void RemovePiece(GameObject piece){if(Application.isPlaying)Destroy(piece);else DestroyImmediate(piece);}
    public void CancelTask(){StopAllCoroutines();resultVisible=false;completed=null;active=false;ReleaseInput();ClearPieces();if(display!=null)display.Hide();}
    private void OnDisable(){CancelTask();}
}
