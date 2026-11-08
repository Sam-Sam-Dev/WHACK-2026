using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

public static class CrewInputCorruptionChecks
{
    [MenuItem("Tools/Mission Marshal/Check Crew Input Corruptions")]
    public static void Run()
    {
        int checks=0;
        Action<bool,string> expect=(ok,label)=>{checks++;if(!ok)throw new Exception("Crew input: "+label);};
        expect(MissionTaskController.GetAsteroidSpeed(0,190,340)==190,"asteroid speed at route start");
        expect(MissionTaskController.GetAsteroidSpeed(.5f,190,340)==265,"asteroid speed halfway through route");
        expect(MissionTaskController.GetAsteroidSpeed(1,190,340)==340,"asteroid speed at route end");
        expect(MissionTaskController.GetAsteroidSpeed(2,190,340)==340 && MissionTaskController.GetAsteroidSpeed(-1,190,340)==190,"speed progress clamped");
        expect(MissionTaskController.GetAsteroidSpeed(1,250,200)==250,"end speed cannot slow below start");
        var random=new System.Random(284);
        for(int id=1;id<=100;id++)
        {
            var rules=CrewInputCorruption.Generate(id,random);
            expect(rules.CrewId==id && rules.KeypadLayout.Length==16,"crew ID and symbol count");
            var symbols=rules.KeypadLayout.ToCharArray();Array.Sort(symbols);
            expect(new string(symbols)=="0123456789ABCDEF","every hex symbol appears once");
            for(int y=0;y<4;y++)for(int x=0;x<4;x++)
                expect(rules.TryGetKeypadSymbol(new Vector2Int(x,y),out char key) && key==rules.KeypadLayout[y*4+x],"keypad coordinate mapping");
        }
        var fixedLayout=new CrewInputCorruption(1,"0123456789ABCDEF");
        expect(fixedLayout.TryGetKeypadSymbol(new Vector2Int(2,1),out char symbol) && symbol=='6',"column/row ordering");
        expect(!fixedLayout.TryGetKeypadSymbol(new Vector2Int(4,0),out _),"invalid coordinates safe");
        var go=new GameObject("Temporary crew and joystick checks");go.SetActive(false);
        try
        {
            var manager=go.AddComponent<GameManager>();manager.AssignCrewVolumes();manager.AssignCrewInputCorruptions();
            expect(manager.CrewInputCorruptions.Count==manager.CrewCount,"one mapping per crew");
            var layouts=new HashSet<string>();
            foreach(var rules in manager.CrewInputCorruptions)expect(layouts.Add(rules.KeypadLayout),"distinct keypad layouts");
            var task=go.AddComponent<MissionTaskController>();
            const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
            typeof(GameManager).GetMethod("ShowTaskSuccessFlash",flags).Invoke(manager,null);
            var flash=(UnityEngine.UI.Image)typeof(GameManager).GetField("successFlash",flags).GetValue(manager);
            expect(flash.gameObject.activeSelf && flash.color.g>flash.color.r && flash.color.a>0,"green screen feedback shown on success");
            expect(!flash.raycastTarget && flash.GetComponentInParent<Canvas>(true).sortingOrder>200,"success flash overlays UI without intercepting input");
            typeof(GameManager).GetField("successFlashEndsAt",flags).SetValue(manager,Time.unscaledTime-1f);
            typeof(GameManager).GetMethod("UpdateTaskSuccessFlash",flags).Invoke(manager,null);
            expect(!flash.gameObject.activeSelf,"success flash clears after duration");
            Action<string,object> set=(name,value)=>typeof(MissionTaskController).GetField(name,flags).SetValue(task,value);
            set("mode",2);set("active",true);set("ready",true);
            var receive=typeof(MissionTaskController).GetMethod("Joystick",flags);
            var axis=typeof(MissionTaskController).GetField("axis",flags);
            // No GameManager/crew mapping is needed to receive joystick input.
            foreach(float x in new[]{-1f,-.7f,0f,.3f,1f})foreach(float y in new[]{-1f,0f,1f})
            {
                receive.Invoke(task,new object[]{new Vector2(x,y)});
                expect((float)axis.GetValue(task)==x,"task uses unmodified X and ignores Y");
                var movement=MissionTaskController.CalculateJoystickMovement(x,420f,.02f);
                expect(Mathf.Approximately(movement.x,x*420f*.02f) && movement.y==0,"movement preserves signed joystick input");
            }
            var components=go.AddComponent<ComponentManager>();
            var receivePanel=typeof(ComponentManager).GetMethod("Receive",flags);
            receivePanel.Invoke(components,new object[]{false,"PANEL ACTIVE component=JOYSTICK"});
            components.SetJoystickCallback(value=>receive.Invoke(task,new object[]{value}));
            foreach(int raw in new[]{0,1024,2048,3071,4095})
            {
                receivePanel.Invoke(components,new object[]{false,$"JOYSTICK POSITION x={raw} y=4095"});
                float expected=PanelSerialProtocol.NormalizeAxis(raw,2047.5f);
                expect((float)axis.GetValue(task)==expected && components.Joystick.x==expected,"serial reading reaches task unchanged after normalization");
            }
            set("game",manager);set("crew",1);
            var crewRules=(List<CrewInputCorruption>)typeof(GameManager).GetField("crewInputCorruptions",flags).GetValue(manager);
            var originalRules=crewRules[0];
            foreach(bool invert in new[]{false,true})
            {
                crewRules[0]=new CrewInputCorruption(1,"0123456789ABCDEF",invert);
                foreach(int raw in new[]{0,1024,2048,3071,4095})
                {
                    receivePanel.Invoke(components,new object[]{false,$"JOYSTICK POSITION x={raw} y=4095"});
                    float normalized=PanelSerialProtocol.NormalizeAxis(raw,2047.5f);
                    float expected=normalized;if(invert)expected=-expected;
                    float actual=(float)axis.GetValue(task);
                    expect(actual==expected,"task applies exactly one optional sign flip");
                    expect(components.Joystick.x==normalized && components.Joystick.y==1f,"crew flip never modifies component data or swaps axes");
                    expect(Mathf.Approximately(MissionTaskController.CalculateJoystickMovement(actual,420f,.02f).x,expected*420f*.02f),"movement does not flip a second time");
                }
            }
            crewRules[0]=originalRules;
            task.asteroidSize=new Vector2(42,42);expect(task.AsteroidCollisionSize==new Vector2(31.5f,31.5f),"25 percent smaller square hitbox");
            task.asteroidSize=new Vector2(60,40);expect(task.AsteroidCollisionSize==new Vector2(30,30),"rectangular sprite gets square hitbox");
            set("active",false);
        }
        finally{UnityEngine.Object.DestroyImmediate(go);}
        Debug.Log($"Crew and direct joystick checks passed: {checks}.");
    }
}
