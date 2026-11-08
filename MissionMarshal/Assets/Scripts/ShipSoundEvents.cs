using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Assign AudioClips per event. Each play creates an independent temporary
/// AudioSource, so sounds can overlap. Cleanup follows actual playback, not game time.</summary>
public sealed class ShipSoundEvents : MonoBehaviour
{
    public enum Cue { MissionStart, Zap, RealmEntered, FingerprintPrompt, FingerprintAccepted,
        FingerprintRejected, TaskStarted, KeyCorrect, KeyWrong, AsteroidImpact,
        GravityPerfect, TaskSuccess, TaskFailure, HeartLost, NormalRealm, Telemetry, Victory, ShipDestroyed, TimerWarning }
    [Serializable] public class Slot
    {
        public Cue cue;
        public AudioClip clip;
        [Range(0f, 1f)] public float volume = 1f;
        [Range(0.1f, 3f)] public float pitch = 1f;
        // Preserve previously assigned prefab sounds while converting the Inspector
        // to clip-only slots. This reference is cleared after copying its sound.
        [SerializeField, HideInInspector, UnityEngine.Serialization.FormerlySerializedAs("prefab")]
        private GameObject legacyPrefab;
        public void Migrate()
        {
            if (legacyPrefab == null) return;
            var source = legacyPrefab.GetComponentInChildren<AudioSource>(true);
            if (clip == null && source != null) { clip = source.clip; volume = source.volume; pitch = Mathf.Clamp(source.pitch, .1f, 3f); }
            legacyPrefab = null;
        }
    }
    public List<Slot> sounds = new List<Slot>();
    public void Play(Cue cue)
    {
        if (sounds == null) return;
        var slot=sounds.Find(s=>s!=null&&s.cue==cue);
        if(slot==null)return;
        slot.Migrate();
        if(slot.clip==null)return;
        var go=new GameObject("Ship Audio - " + cue);
        var source=go.AddComponent<AudioSource>();
        source.playOnAwake=false;source.loop=false;source.spatialBlend=0f;
        source.clip=slot.clip;source.volume=Mathf.Clamp01(slot.volume);source.pitch=Mathf.Clamp(slot.pitch,.1f,3f);
        go.AddComponent<ShipOneShotAudio>(); // Plays and destroys itself when finished.
    }
    private void OnValidate(){if(sounds!=null)foreach(var slot in sounds)slot?.Migrate();}
    [ContextMenu("Add All Sound Slots")]
    public void AddAllSlots(){foreach(Cue cue in Enum.GetValues(typeof(Cue)))if(!sounds.Exists(s=>s!=null&&s.cue==cue))sounds.Add(new Slot{cue=cue});}
}
