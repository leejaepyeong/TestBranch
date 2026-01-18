using UnityEngine;

public enum SoundBus
{
    BGM,
    BGM_MAP,
    BGM_DIALOG,
    SFX,
    UI,
    Voice
}

public class SoundCategory : MonoBehaviour
{
    public SoundBus bus = SoundBus.SFX; // ±âº»°ª
}
