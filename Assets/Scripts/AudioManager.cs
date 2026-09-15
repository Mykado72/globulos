using UnityEngine;

/// ✅ Gestionnaire audio central (singleton, même pattern que UIManager).
/// - Sons "positionnels" (tir, rebond, chute de bille) : joués via
///   AudioSource.PlayClipAtPoint, pas besoin d'AudioSource sur chaque bille.
/// - Sons "globaux" (fin de partie, but au ballon) : joués sur un AudioSource
///   persistant, pour survivre aux ~2s de délai avant le reload de scène en fin
///   de partie (voir ReloadSceneAfterDelay dans TurnManager_GameEndLogic).
///
/// Place ce script sur un GameObject unique dans ta scène (Lobby ou GameScene,
/// peu importe grâce au DontDestroyOnLoad + pattern singleton) et assigne tes
/// clips audio dans l'inspecteur.
public class AudioManager : MonoBehaviour
{
    public static AudioManager Instance { get; private set; }

    [Header("Bille : Tir")]
    [Tooltip("Un ou plusieurs clips, un est choisi au hasard à chaque tir pour éviter la répétition.")]
    [SerializeField] private AudioClip[] shootClips;

    [Header("Bille : Rebond")]
    [Tooltip("Un ou plusieurs clips, un est choisi au hasard à chaque rebond.")]
    [SerializeField] private AudioClip[] bounceClips;

    [Header("Bille : Chute dans un but")]
    [SerializeField] private AudioClip ballDeathClip;

    [Header("Ballon de foot : but marqué")]
    [SerializeField] private AudioClip soccerGoalClip;

    [Header("Fin de partie")]
    [SerializeField] private AudioClip winClip;
    [SerializeField] private AudioClip drawClip;

    [Header("Réglages généraux")]
    [SerializeField, Range(0f, 1f)] private float sfxVolume = 1f;

    // AudioSource dédié aux sons globaux (non positionnels), doit survivre au reload de scène.
    private AudioSource _globalSource;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);

            _globalSource = gameObject.AddComponent<AudioSource>();
            _globalSource.playOnAwake = false;
            _globalSource.spatialBlend = 0f; // 2D, non positionnel
        }
        else
        {
            Destroy(gameObject);
        }
    }

    // --- Bille : sons positionnels ---

    public void PlayShoot(Vector3 position)
    {
        PlayRandomAtPoint(shootClips, position);
    }

    public void PlayBounce(Vector3 position)
    {
        PlayRandomAtPoint(bounceClips, position);
    }

    public void PlayBallDeath(Vector3 position)
    {
        PlayOneAtPoint(ballDeathClip, position);
    }

    // --- Événements globaux (non positionnels) ---

    public void PlaySoccerGoal()
    {
        PlayGlobal(soccerGoalClip);
    }

    public void PlayWin()
    {
        PlayGlobal(winClip);
    }

    public void PlayDraw()
    {
        PlayGlobal(drawClip);
    }

    // --- Implémentation ---

    private void PlayGlobal(AudioClip clip)
    {
        if (clip == null || _globalSource == null) return;
        _globalSource.PlayOneShot(clip, sfxVolume);
    }

    private void PlayOneAtPoint(AudioClip clip, Vector3 position)
    {
        if (clip == null) return;
        AudioSource.PlayClipAtPoint(clip, position, sfxVolume);
    }

    private void PlayRandomAtPoint(AudioClip[] clips, Vector3 position)
    {
        if (clips == null || clips.Length == 0) return;
        AudioClip clip = clips[Random.Range(0, clips.Length)];
        PlayOneAtPoint(clip, position);
    }
}
