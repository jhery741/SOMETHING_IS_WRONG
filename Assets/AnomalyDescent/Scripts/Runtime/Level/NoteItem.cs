using System.Collections;
using UnityEngine;

namespace SomethingIsWrong
{
    /// <summary>Pista narrativa: una nota que se lee o una grabación que se reproduce (con subtítulos).</summary>
    public class NoteItem : MonoBehaviour, IInteractable
    {
        public string title = "Nota";
        [TextArea(3, 10)] public string body;
        public bool isRecording;
        public string[] lines;
        public bool collected;
        bool playing;

        public string GetPrompt(PlayerController p)
        {
            if (isRecording) return playing ? "Reproduciendo..." : "[E] Reproducir grabación (hace ruido)";
            return "[E] Leer: " + title;
        }

        public void Interact(PlayerController p)
        {
            if (!collected)
            {
                collected = true;
                if (GameDirector.Instance != null) GameDirector.Instance.OnClueFound(this);
            }
            if (isRecording) { if (!playing) StartCoroutine(Play()); }
            else if (p.Interaction != null) p.Interaction.OpenNote(this);
        }

        IEnumerator Play()
        {
            playing = true;
            var src = SoundBank.AddLoop(gameObject, "static", 0.25f, 10f, true);
            if (src != null && src.clip != null) src.Play();
            NoiseSystem.Emit(transform.position, 4f, true);
            if (lines != null)
            {
                foreach (var l in lines)
                {
                    HUD.Subtitle(l, 3.6f);
                    yield return new WaitForSeconds(3.6f);
                }
            }
            if (src != null) Destroy(src);
            playing = false;
        }
    }
}
