using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using YukkuriMovieMaker.UndoRedo;

namespace YMM4GameHub
{
    /// <summary>
    /// レベルシステムを管理するシングルトンクラス。
    /// - UndoRedoManager の UndoRedoCommandCreated で編集を検知してポイントを加算
    /// - ゲーム終了時にポイントを減算
    /// - レベルが大きくなるほど変化しにくいスケーリング
    /// - データはメモリに保持し Hub が閉じたときに SettingsBase へ書き込む
    /// </summary>
    public static class LevelManager
    {
        // ---- 設定定数 ----
        private const double BASE_EDIT_POINTS_PER_MIN = 0.2;   // 5分で1.0ポイント
        private const double BASE_GAME_PENALTY = 1.0;

        private static double Threshold(int level) => 1.0 * Math.Pow(Math.Max(1, Math.Abs(level)), 1.7);

        // ---- 状態 ----
        private static bool _isHubVisible = false;
        private static bool _isPlaying = false;
        private static bool _isEditing = false;
        private static DateTime _lastEditTime = DateTime.MinValue;
        private static readonly System.Windows.Threading.DispatcherTimer _timer = new();
        private static UndoRedoManager? _undoRedoManager;
        private static System.Windows.Threading.DispatcherTimer? _editCooldownTimer;

        // インメモリキャッシュ（Settingsへの書き込みは Hub が閉じたときだけ）
        private static LevelData _cache = LoadFromSettings();

        private const int TIMER_INTERVAL_SEC = 5;
        private const int EDIT_COOLDOWN_SEC = 300;

        static LevelManager()
        {
            _timer.Interval = TimeSpan.FromSeconds(TIMER_INTERVAL_SEC);
            _timer.Tick += OnTimerTick;
            _timer.Start();
        }

        // ---- Public API ----

        public static void SetHubVisible(bool visible)
        {
            _isHubVisible = visible;
        }

        public static void SetGameActive(bool active)
        {
            _isPlaying = active;
        }

        public static void SetUndoRedoManager(UndoRedoManager? manager)
        {
            if (_undoRedoManager != null)
                _undoRedoManager.UndoRedoCommandCreated -= OnUndoRedoCommandCreated;
            _undoRedoManager = manager;
            if (_undoRedoManager != null)
                _undoRedoManager.UndoRedoCommandCreated += OnUndoRedoCommandCreated;
        }

        public static bool IsGainingPoints => !_isPlaying && (_isEditing || (DateTime.Now - _lastEditTime).TotalSeconds < EDIT_COOLDOWN_SEC);

        public static event Action? PenaltyApplied;

        public static void ApplyGamePenalty()
        {
            int absLevel = Math.Abs(_cache.Level);
            double scaledPenalty = BASE_GAME_PENALTY * ScaleFactor(absLevel);
            _cache.Points -= scaledPenalty;
            CheckLevelChange();
            FlushToSettings();
            PenaltyApplied?.Invoke();
        }

        /// <summary>現在のレベルデータを返す（UI表示用）。</summary>
        public static LevelData GetData() => _cache;

        /// <summary>現在レベルでのアップ閾値を返す（プログレスバー用）。</summary>
        public static double GetThreshold() => Threshold(_cache.Level);

        /// <summary>Settings へ即時書き込む。</summary>
        public static void FlushToSettings() => SaveToSettings(_cache);

        // ---- Private ----

        private static void OnTimerTick(object? sender, EventArgs e)
        {
            if (_isPlaying) return;

            bool recentEdit = _isEditing || (DateTime.Now - _lastEditTime).TotalSeconds < EDIT_COOLDOWN_SEC;
            if (!recentEdit) return;

            int absLevel = Math.Abs(_cache.Level);
            double pointsPerTick = (BASE_EDIT_POINTS_PER_MIN * TIMER_INTERVAL_SEC / 60.0) * ScaleFactor(absLevel);
            _cache.Points += pointsPerTick;
            CheckLevelChange();
            // メモリ更新のみ → UI通知（Settings保存は Hub 閉鎖時）
            LevelChanged?.Invoke(null, _cache);
        }

        private static void OnUndoRedoCommandCreated(object? sender, EventArgs e)
        {
            _lastEditTime = DateTime.Now;
            _isEditing = true;

            _editCooldownTimer?.Stop();
            _editCooldownTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(EDIT_COOLDOWN_SEC)
            };
            _editCooldownTimer.Tick += (s, e) =>
            {
                _isEditing = false;
                _editCooldownTimer?.Stop();
            };
            _editCooldownTimer.Start();
        }

        private static void CheckLevelChange()
        {
            bool changed = false;
            while (true)
            {
                double threshold = Threshold(_cache.Level);
                if (_cache.Points >= threshold)
                {
                    _cache.Points -= threshold;
                    _cache.Level++;
                    changed = true;
                }
                else if (_cache.Points < 0)
                {
                    _cache.Level--;
                    double prevThreshold = Threshold(_cache.Level);
                    _cache.Points += prevThreshold;
                    changed = true;
                }
                else break;
            }

            if (changed)
            {
                System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
                    LevelChanged?.Invoke(null, _cache));
            }
        }

        public static event EventHandler<LevelData>? LevelChanged;

        private static double ScaleFactor(int absLevel) =>
            1.0 / (1.0 + absLevel * 0.1);

        // ---- Persistence ----

        private static readonly byte[] _aesKey = Encoding.UTF8.GetBytes("YMM4GameHub!K3y!");
        private static readonly byte[] _aesIv  = Encoding.UTF8.GetBytes("GameHubLevel!IV!");

        private static LevelData LoadFromSettings()
        {
            try
            {
                var encoded = YMM4GameHubSettings.Default.UserSettings.EncodedLevelData;
                if (string.IsNullOrEmpty(encoded)) return new LevelData();
                var cipher = Convert.FromBase64String(encoded);
                using var aes = Aes.Create();
                aes.Key = _aesKey;
                aes.IV  = _aesIv;
                using var dec = aes.CreateDecryptor();
                var plain = dec.TransformFinalBlock(cipher, 0, cipher.Length);
                return JsonSerializer.Deserialize<LevelData>(Encoding.UTF8.GetString(plain)) ?? new LevelData();
            }
            catch { return new LevelData(); }
        }

        private static void SaveToSettings(LevelData data)
        {
            try
            {
                var plain = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(data));
                using var aes = Aes.Create();
                aes.Key = _aesKey;
                aes.IV  = _aesIv;
                using var enc = aes.CreateEncryptor();
                YMM4GameHubSettings.Default.UserSettings.EncodedLevelData =
                    Convert.ToBase64String(enc.TransformFinalBlock(plain, 0, plain.Length));
            }
            catch { }
        }

        // 旧API互換（呼び出し箇所があれば）
        public static LevelData LoadData() => _cache;
        public static void SaveData(LevelData _) => FlushToSettings();
    }

    public class LevelData
    {
        public int Level { get; set; } = 1;
        public double Points { get; set; } = 0.0;
    }
}
