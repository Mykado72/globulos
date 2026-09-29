using System.Collections;
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

    [Header("Timer")]
    [Tooltip("Tic-tac joué à chaque seconde entre 'Tick From Seconds' et 'Countdown From Seconds'.")]
    [SerializeField] private AudioClip timerTickClip;
    [Tooltip("Bip du compte à rebours final (dernières secondes). Si vide, le tic-tac est utilisé.")]
    [SerializeField] private AudioClip timerCountdownClip;
    [Tooltip("Son joué quand le temps est écoulé.")]
    [SerializeField] private AudioClip timeUpClip;
    [Tooltip("Le tic-tac démarre quand il reste ce nombre de secondes (ou moins).")]
    [SerializeField, Min(1)] private int tickFromSeconds = 10;
    [Tooltip("Le compte à rebours final démarre quand il reste ce nombre de secondes (3 = comme l'effet rouge du timer).")]
    [SerializeField, Min(1)] private int countdownFromSeconds = 3;
    [SerializeField, Range(0f, 1f)] private float timerVolume = 0.8f;

    [Header("But encaissé (animation LOOSER)")]
    [SerializeField] private AudioClip loserClip;

    [Header("Foule (bruit de fond lors des buts)")]
    [Tooltip("Ambiance de foule jouée pendant l'animation de but, pour le buteur comme pour celui qui encaisse.")]
    [SerializeField] private AudioClip goalCrowdClip;
    [SerializeField, Range(0f, 1f)] private float crowdVolume = 0.6f;
    [Tooltip("Durée du fondu de sortie de la foule, à la fin de l'animation.")]
    [SerializeField, Min(0f)] private float crowdFadeOutSeconds = 1f;

    [Header("Réglages généraux")]
    [SerializeField, Range(0f, 1f)] private float sfxVolume = 1f;

    // AudioSource dédié aux sons globaux (non positionnels), doit survivre au reload de scène.
    private AudioSource _globalSource;

    // AudioSource dédié à la foule : volume et fondu indépendants des autres sons.
    private AudioSource _crowdSource;
    private Coroutine _crowdRoutine;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);

            _globalSource = gameObject.AddComponent<AudioSource>();
            _globalSource.playOnAwake = false;
            _globalSource.spatialBlend = 0f; // 2D, non positionnel

            _crowdSource = gameObject.AddComponent<AudioSource>();
            _crowdSource.playOnAwake = false;
            _crowdSource.loop = false;
            _crowdSource.spatialBlend = 0f;
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

    // --- Timer ---

    /// <summary>Appelé à chaque seconde qui passe. Ne joue rien tant qu'on est au-dessus de 'Tick From Seconds'.</summary>
    public void PlayTimerTick(int secondsLeft)
    {
        if (secondsLeft < 1 || secondsLeft > tickFromSeconds) return;

        AudioClip clip = (secondsLeft <= countdownFromSeconds && timerCountdownClip != null)
            ? timerCountdownClip
            : timerTickClip;
        PlayGlobal(clip, timerVolume);
    }

    /// <summary>Le temps du tour est écoulé.</summary>
    public void PlayTimeUp()
    {
        PlayGlobal(timeUpClip, timerVolume);
    }

    // --- But : loser et foule ---

    /// <summary>Son de défaite pour le joueur qui vient d'encaisser un but.</summary>
    public void PlayLoser()
    {
        PlayGlobal(loserClip);
    }

    /// <summary>
    /// Foule en fond pendant l'animation de but. Fondu de sortie sur les dernières secondes de
    /// 'totalDurationSeconds' (durée de l'animation). Un nouveau but remplace la foule précédente.
    /// </summary>
    public void PlayGoalCrowd(float totalDurationSeconds)
    {
        if (goalCrowdClip == null || _crowdSource == null) return;

        if (_crowdRoutine != null) StopCoroutine(_crowdRoutine);
        _crowdSource.Stop();
        _crowdSource.clip = goalCrowdClip;
        _crowdSource.volume = crowdVolume * sfxVolume;
        _crowdSource.Play();

        if (totalDurationSeconds > 0f)
            _crowdRoutine = StartCoroutine(CrowdFadeOutRoutine(totalDurationSeconds));
    }

    private IEnumerator CrowdFadeOutRoutine(float totalDurationSeconds)
    {
        float fade = Mathf.Min(crowdFadeOutSeconds, totalDurationSeconds);
        yield return new WaitForSeconds(totalDurationSeconds - fade);

        float startVolume = _crowdSource.volume;
        float elapsed = 0f;
        while (elapsed < fade && _crowdSource.isPlaying)
        {
            elapsed += Time.deltaTime;
            _crowdSource.volume = Mathf.Lerp(startVolume, 0f, elapsed / fade);
            yield return null;
        }

        _crowdSource.Stop();
        _crowdRoutine = null;
    }

    // --- Implémentation ---

    private void PlayGlobal(AudioClip clip, float volumeMultiplier = 1f)
    {
        if (clip == null || _globalSource == null) return;
        _globalSource.PlayOneShot(clip, sfxVolume * volumeMultiplier);
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
