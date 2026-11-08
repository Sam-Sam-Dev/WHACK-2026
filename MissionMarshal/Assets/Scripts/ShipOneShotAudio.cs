using System.Collections;
using UnityEngine;

/// <summary>Temporary event audio. Lives independently of the event emitter so a
/// scene change or disabled manager does not strand cleanup. AudioListener pauses
/// are respected; Time.timeScale does not affect playback completion detection.</summary>
[RequireComponent(typeof(AudioSource))]
public sealed class ShipOneShotAudio : MonoBehaviour
{
    private IEnumerator Start()
    {
        var source=GetComponent<AudioSource>();
        var clip=source.clip;
        if(clip==null){Destroy(gameObject);yield break;}
        if(clip.loadState==AudioDataLoadState.Unloaded)clip.LoadAudioData();
        while(clip.loadState==AudioDataLoadState.Loading)yield return null;
        if(clip.loadState!=AudioDataLoadState.Loaded){Destroy(gameObject);yield break;}
        source.Play();
        while(source!=null&&(source.isPlaying||(AudioListener.pause&&!source.ignoreListenerPause)))yield return null;
        Destroy(gameObject);
    }
}
