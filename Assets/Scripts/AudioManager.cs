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
[System.Serializable]
public class MusicTrack
{
    [Tooltip("Nom affiché dans l'interface (menu de sélection).")]
    public string displayName;
    [Tooltip("Chemin du mp3 depuis StreamingAssets, ex : Music/game.mp3")]
    public string fileName;
}

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

    [Header("Musique de fond (lue par la page web via WebMusic)")]
    [Tooltip("Morceaux disponibles. 'File Name' = chemin depuis Assets/StreamingAssets/ (ex : Music/game.mp3).")]
    [SerializeField] private MusicTrack[] musicTracks;
    [Tooltip("Index du morceau joué au démarrage si aucun choix n'a été sauvegardé. Laisser décoché si la page index.html lance déjà son morceau par défaut.")]
    [SerializeField] private bool playMusicOnStart = false;
    [SerializeField, Min(0)] private int startTrackIndex = 0;
    [SerializeField, Range(0f, 1f)] private float musicVolume = 0.5f;
    [SerializeField] private bool musicLoop = true;

    private const string MusicIndexPrefKey = "AudioManager.MusicTrackIndex";
    private const string MusicVolumePrefKey = "AudioManager.MusicVolume";
    private int _currentMusicIndex = -1;

    [Header("Réglages généraux")]
    [SerializeField, Range(0f, 1f)] private float sfxVolume = 1f;

    [Header("Sons de bille (tir / rebond / chute)")]
    [Tooltip("Nombre de sources 2D en rotation pour les sons de bille. Augmenter si des sons se coupent.")]
    [SerializeField, Min(2)] private int marblePoolSize = 8;
    [Tooltip("Multiplicateur de volume des sons de bille (rebonds, tir, chute). Volume FIXE : ne dépend ni de la distance ni de la vitesse.")]
    [SerializeField, Range(0f, 2f)] private float marbleVolume = 1f;

    // AudioSource dédié aux sons globaux (non positionnels), doit survivre au reload de scène.
    private AudioSource _globalSource;

    // Pool de sources 2D pour les sons de bille (remplace PlayClipAtPoint, qui est en 3D).
    private AudioSource[] _marblePool;
    private int _marblePoolIndex;

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

            _marblePool = new AudioSource[marblePoolSize];
            for (int i = 0; i < _marblePool.Length; i++)
            {
                AudioSource src = gameObject.AddComponent<AudioSource>();
                src.playOnAwake = false;
                src.loop = false;
                src.spatialBlend = 0f; // 2D : aucune atténuation selon la distance à la caméra
                src.priority = 0;      // priorité max : jamais volé par un autre son
                _marblePool[i] = src;
            }
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

    // --- Musique de fond ---

    private void Start()
    {
        if (PlayerPrefs.HasKey(MusicVolumePrefKey))
            musicVolume = PlayerPrefs.GetFloat(MusicVolumePrefKey);
        WebMusic.SetVolume(musicVolume);

        // Un choix sauvegardé est restauré. Sinon, on ne lance un morceau que si demandé :
        // index.html joue déjà son propre morceau par défaut dès l'ouverture de la page.
        if (PlayerPrefs.HasKey(MusicIndexPrefKey))
            PlayMusic(PlayerPrefs.GetInt(MusicIndexPrefKey), save: false);
        else if (playMusicOnStart)
            PlayMusic(startTrackIndex, save: false);
    }

    public int MusicTrackCount => musicTracks != null ? musicTracks.Length : 0;
    public int CurrentMusicIndex => _currentMusicIndex;
    public float MusicVolume => musicVolume;

    /// <summary>Nom affiché d'un morceau (pour remplir un Dropdown, par exemple).</summary>
    public string GetMusicTrackName(int index)
    {
        if (index < 0 || index >= MusicTrackCount) return string.Empty;
        MusicTrack t = musicTracks[index];
        return string.IsNullOrEmpty(t.displayName) ? t.fileName : t.displayName;
    }

    /// <summary>Joue le morceau d'index donné. Le choix est sauvegardé (PlayerPrefs).</summary>
    public void PlayMusic(int index) => PlayMusic(index, save: true);

    private void PlayMusic(int index, bool save)
    {
        if (index < 0 || index >= MusicTrackCount)
        {
            Debug.LogWarning($"[AudioManager] Index de musique invalide : {index}");
            return;
        }

        MusicTrack track = musicTracks[index];
        if (string.IsNullOrEmpty(track.fileName)) return;

        _currentMusicIndex = index;
        WebMusic.Play(track.fileName, musicLoop);
        WebMusic.SetVolume(musicVolume);

        if (save)
        {
            PlayerPrefs.SetInt(MusicIndexPrefKey, index);
            PlayerPrefs.Save();
        }
    }

    /// <summary>Joue un morceau d'après son nom affiché ou son fichier.</summary>
    public void PlayMusic(string nameOrFile)
    {
        for (int i = 0; i < MusicTrackCount; i++)
        {
            if (musicTracks[i].displayName == nameOrFile || musicTracks[i].fileName == nameOrFile)
            {
                PlayMusic(i);
                return;
            }
        }
        Debug.LogWarning($"[AudioManager] Morceau introuvable : {nameOrFile}");
    }

    public void NextMusic()
    {
        if (MusicTrackCount == 0) return;
        PlayMusic((_currentMusicIndex + 1 + MusicTrackCount) % MusicTrackCount);
    }

    public void PreviousMusic()
    {
        if (MusicTrackCount == 0) return;
        int prev = _currentMusicIndex <= 0 ? MusicTrackCount - 1 : _currentMusicIndex - 1;
        PlayMusic(prev);
    }

    public void StopMusic()
    {
        WebMusic.Stop();
    }

    public void SetMusicVolume(float volume01)
    {
        musicVolume = Mathf.Clamp01(volume01);
        WebMusic.SetVolume(musicVolume);
        PlayerPrefs.SetFloat(MusicVolumePrefKey, musicVolume);
    }

    // --- Implémentation ---

    private void PlayGlobal(AudioClip clip, float volumeMultiplier = 1f)
    {
        if (clip == null || _globalSource == null) return;
        _globalSource.PlayOneShot(clip, sfxVolume * volumeMultiplier);
    }

    // ✅ FIX : AudioSource.PlayClipAtPoint crée une source 3D (spatialBlend = 1). Dans un jeu 2D
    // dont la caméra est à z = -10, le son subit donc une atténuation par la distance (~1/10 du
    // volume), qui varie en plus selon la position X/Y de la bille. D'où des sons trop faibles
    // et à volume variable. On passe par un pool de sources 2D : volume constant et audible.
    // 'position' est conservé dans la signature pour ne pas toucher aux appelants.
    private void PlayOneAtPoint(AudioClip clip, Vector3 position)
    {
        if (clip == null || _marblePool == null) return;

        AudioSource src = _marblePool[_marblePoolIndex];
        _marblePoolIndex = (_marblePoolIndex + 1) % _marblePool.Length;
        src.PlayOneShot(clip, sfxVolume * marbleVolume);
    }

    private void PlayRandomAtPoint(AudioClip[] clips, Vector3 position)
    {
        if (clips == null || clips.Length == 0) return;
        AudioClip clip = clips[Random.Range(0, clips.Length)];
        PlayOneAtPoint(clip, position);
    }
}
