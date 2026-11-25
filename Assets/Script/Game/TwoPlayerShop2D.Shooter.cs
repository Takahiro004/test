using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

// 特殊モード「ネオン射撃」(サイバーパンク風のときに遊ぶ射的ミニゲーム。TwoPlayerShop2D の続き。
// 設定の欄は TwoPlayerShop2D.cs の「特殊モード: ネオン射撃」)
// サイバーパンク風(かくしコマンド 2077。TwoPlayerShop2D.Cyber.cs)のままエントリーして始めると、詰め放題バトルの代わりにこれを遊ぶ。
//  - 1P/2P の画面: 自分の射撃場。奥にネオンの街、手前にかくれる壁、的(マト)が出てくる
//  - 真ん中の画面: 残り時間・2人の点数・弾の数など
//  - 操作: 左スティックで照準を動かす / A を押しているあいだ、壁から顔を出して撃ち続ける / 離すと壁にかくれる
//    かくれているあいだに弾が補充される(リロード)
//  - 的はときどき撃ってくる(赤い輪が縮んで「!」)。そのとき顔を出していると、点数が減って少し撃てなくなる。
//    かくれていれば「ガード」
//  - 的に当てると +10、こわすと +100。時間切れで点数の多いほうの勝ち。A でもう一度(シーンを読み込み直す)
//  - キーボード予備: 1P WASD + Space / 2P 矢印 + Enter
// 仕組み: 詰め放題バトルの物はそのまま止めておき、3つのカメラを遠く(ShooterOrigin)へ動かして、そこに作った射撃場を映す。
// シーンは書き換えない(全部スクリプトで作る)。
// エントリー画面も、サイバーパンク風なら射撃場の上に出す(UpdateShooterEntry。照準を動かす・A で顔を出す練習ができる)。
public partial class TwoPlayerShop2D
{
    static readonly Vector3 ShooterOrigin = new Vector3(10000f, 0f, 0f);   // 射撃場を作る場所(詰め放題の物から遠く)

    bool shooterOn;          // 射撃中(時間切れのあとも true のまま)
    bool shooterEnded;
    float shooterLeft;
    float shooterEndTime;
    Transform shooterRoot;
    Shooter[] shooters;
    TextMesh shTitle, shTimer, shCenter;
    readonly TextMesh[] shScore = new TextMesh[2];
    readonly TextMesh[] shInfo = new TextMesh[2];
    SpriteRenderer shBarBack, shBar1, shBar2;
    readonly List<ShPop> shPops = new List<ShPop>();
    AudioSource shAudio;
    static Sprite shTargetSprite, shCrossSprite, shRingSprite, shCoverSprite;
    static AudioClip shShotClip, shHitClip, shBreakClip, shDamageClip;

    class ShTarget
    {
        public Transform tr;
        public SpriteRenderer sr, ring;
        public TextMesh mark;
        public Vector2 pos, vel;
        public float size;
        public int hp;
        public float attackIn, life, hitFlash, appear;
        public bool leaving;
    }

    class Shooter
    {
        public int index;
        public Player p;
        public int layer;
        public Vector2 aim;
        public bool peek;
        public int ammo;
        public float reload, fireCool, stun, damage, shake;
        public int score;
        public float coverK;     // 0 = 顔を出している / 1 = かくれている
        public float nextSpawn;
        public Transform cross;
        public SpriteRenderer crossSr, cover, redFlash;
        public TextMesh state;
        public GlitchFx glitch;
        public readonly List<ShTarget> targets = new List<ShTarget>();
    }

    class ShPop
    {
        public TextMesh tm;
        public float t, life;
        public Vector3 vel;
    }

    // エントリーのカウントダウンが終わったときに呼ぶ(サイバーパンク風なら、詰め放題の代わりに射撃を始める)
    bool TryStartShooter()
    {
        if (!cyberTheme || !shooterMode || shooterOn) return false;
        shooterOn = true;
        shooterLeft = Mathf.Max(5f, shooterTime);
        if (shooterRoot == null) BuildShooter();
        Vector2 sv = ShopViewSize();
        ShPopup(layerShop, "START!", Vector2.zero, new Color(1f, 0.9f, 0.3f), FitSize("START!", sv.x * 0.8f, sv.y * 0.3f, Mathf.Min(sv.x, sv.y) / 10f), 1f);
        foreach (var s in shooters) s.state.text = "";
        lastEvent = "ネオン射撃 開始！";
        return true;
    }

    // エントリー画面のあいだ(UpdateEntry から毎フレーム): サイバーパンク風なら、射撃場を先に作ってエントリー画面にする
    void UpdateShooterEntry()
    {
        bool want = cyberTheme && shooterMode && InEntry;
        if (want && shooterRoot == null && !cyberSwitching) BuildShooter();
        if (!want && shooterRoot != null && !shooterOn) DestroyShooter();
        if (shooterRoot == null) return;

        float dt = Time.deltaTime;
        foreach (var s in shooters) StepShooter(s, dt);   // 照準を動かす・A で顔を出すのは練習できる(的は出ない)
        StepShPops(dt);
        RefreshShooterShop();

        Vector2 pv = PlayerViewSize(), sv = ShopViewSize();
        for (int i = 0; i < 2; i++)
        {
            var s = shooters[i];
            s.state.text = entryPhase == 2 ? "" : entryReady[i] ? $"{s.p.name}  参加OK！\n(Bで取り消し)" : $"{s.p.name}\nAで参加";
            s.state.color = entryReady[i] ? s.p.color : Color.white;
            SetTextSize(s.state, FitSize(s.state.text, pv.x * 0.85f, pv.y * 0.2f, Mathf.Min(pv.x, pv.y) / 14f));
            s.state.transform.position = ShooterOrigin + new Vector3(0f, -pv.y * 0.32f, -2f);
        }
        if (entryPhase == 2)
        {
            shTimer.text = Mathf.CeilToInt(entryCountdownLeft).ToString();
            shCenter.text = "";
        }
        else
        {
            shTimer.text = "READY?";
            shCenter.text = entryReady[0] && entryReady[1] ? "" : "ふたりそろったらスタート";
        }
        LayoutShooterShop();
    }

    // 射撃場を消して、カメラを詰め放題の場所に戻す(エントリー画面で、かくしコマンドをもう一度打って元に戻したとき)
    void DestroyShooter()
    {
        if (shooters != null) foreach (var s in shooters) if (s.glitch != null) Destroy(s.glitch.root.gameObject);
        foreach (var p in shPops) if (p.tm != null) Destroy(p.tm.gameObject);
        shPops.Clear();
        if (shooterRoot != null) Destroy(shooterRoot.gameObject);
        shooterRoot = null;
        shooters = null;
        if (switcher != null && switcher.screens != null)
            foreach (var kv in shCamBase)
                if (kv.Key >= 0 && kv.Key < switcher.screens.Length && switcher.screens[kv.Key].camera != null)
                    switcher.screens[kv.Key].camera.transform.position = kv.Value;
        shCamBase.Clear();
    }

    readonly Dictionary<int, Vector3> shCamBase = new Dictionary<int, Vector3>();   // 動かす前の 1P/2P のカメラの位置

    // ---------- 文字の大きさ(画面からはみ出さないように) ----------

    // 文字の幅(全角 = 1、半角 = 0.6 として、いちばん長い行)
    static float TextWidthUnits(string text)
    {
        float best = 0f, cur = 0f;
        foreach (char c in text)
        {
            if (c == '\n') { best = Mathf.Max(best, cur); cur = 0f; continue; }
            cur += c < 128 ? 0.6f : 1f;
        }
        return Mathf.Max(best, cur, 0.6f);
    }

    // 幅 maxW・高さ maxH に収まる文字の大きさ(cap より大きくしない)。SetTextSize の大きさ 1 で、1文字が約 6.4
    static float FitSize(string text, float maxW, float maxH, float cap)
    {
        int lines = string.IsNullOrEmpty(text) ? 1 : text.Split('\n').Length;
        float size = Mathf.Min(cap, maxW / (6.4f * TextWidthUnits(text ?? "")));
        return Mathf.Min(size, maxH / (6.4f * 1.15f * lines));
    }

    void Fit(TextMesh tm, float maxW, float maxH, float cap) => SetTextSize(tm, FitSize(tm.text, maxW, maxH, cap));

    // ---------- 作る ----------

    void BuildShooter()
    {
        shooterRoot = new GameObject("NeonShooter").transform;
        shooterRoot.position = ShooterOrigin;
        shAudio = shooterRoot.gameObject.AddComponent<AudioSource>();
        shAudio.playOnAwake = false;

        Vector2 pv = PlayerViewSize();
        shooters = new Shooter[2];
        for (int i = 0; i < 2; i++)
        {
            var p = i == 0 ? p1 : p2;
            var s = new Shooter { index = i, p = p, layer = p.layer, ammo = Mathf.Max(1, shooterAmmo), coverK = 1f, nextSpawn = 0.5f };
            // 奥の絵(ネオンの街)
            var back = ShSprite("ShooterBack", p.layer, cyberBackgroundSprite != null ? cyberBackgroundSprite : GetCyberBackSprite(), 0);
            Vector2 bs = back.sprite.bounds.size;
            float k = Mathf.Max(pv.x / bs.x, pv.y / bs.y);
            back.transform.localScale = new Vector3(k, k, 1f);
            // かくれる壁(上のふちを動かす)
            s.cover = ShSprite("Cover", p.layer, GetShCoverSprite(), 300);
            // 照準
            s.crossSr = ShSprite("Crosshair", p.layer, GetShCrossSprite(), 400);
            s.cross = s.crossSr.transform;
            float cs = Mathf.Min(pv.x, pv.y) * 0.12f;
            s.cross.localScale = new Vector3(cs / s.crossSr.sprite.bounds.size.x, cs / s.crossSr.sprite.bounds.size.y, 1f);
            s.aim = new Vector2(0f, pv.y * 0.1f);
            // 撃たれたときの赤
            s.redFlash = ShSprite("Damage", p.layer, GetSquareSprite(), 450);
            s.redFlash.transform.localScale = new Vector3(pv.x / s.redFlash.sprite.bounds.size.x, pv.y / s.redFlash.sprite.bounds.size.y, 1f);
            s.redFlash.color = new Color(1f, 0f, 0.1f, 0f);
            // 走査線
            if (cyberScanAlpha > 0f)
            {
                var scan = ShSprite("Scan", p.layer, GetScanlineSprite(), 440);
                Vector2 ss = scan.sprite.bounds.size;
                scan.transform.localScale = new Vector3(pv.x / ss.x, pv.y / ss.y, 1f);
                scan.color = new Color(1f, 1f, 1f, cyberScanAlpha);
            }
            // 画面の下の文字(弾・リロード・撃たれた など)
            s.state = MakeText("ShooterState", shooterRoot, p.layer, "", Color.white, 460);
            s.state.transform.position = ShooterOrigin + new Vector3(0f, -pv.y * 0.4f, -2f);
            s.glitch = MakeGlitch(p.layer, false);
            shooters[i] = s;
        }

        // 真ん中の画面(点数など)
        Vector2 sv = ShopViewSize();
        var shopBack = ShSprite("ShopBack", layerShop, GetSquareSprite(), 0);
        shopBack.transform.localScale = new Vector3(sv.x / shopBack.sprite.bounds.size.x, sv.y / shopBack.sprite.bounds.size.y, 1f);
        shopBack.color = new Color(cyberBackColor.r, cyberBackColor.g, cyberBackColor.b, 1f);
        if (cyberScanAlpha > 0f)
        {
            var scan = ShSprite("ShopScan", layerShop, GetScanlineSprite(), 440);
            Vector2 ss = scan.sprite.bounds.size;
            scan.transform.localScale = new Vector3(sv.x / ss.x, sv.y / ss.y, 1f);
            scan.color = new Color(1f, 1f, 1f, cyberScanAlpha);
        }
        shTitle = ShText("Title", layerShop, shooterTitle, new Color(1f, 0.2f, 0.8f), sv.x / 45f, new Vector2(0f, sv.y * 0.38f));
        shTimer = ShText("Timer", layerShop, "", new Color(0.3f, 1f, 1f), sv.x / 22f, new Vector2(0f, sv.y * 0.24f));
        for (int i = 0; i < 2; i++)
        {
            var p = i == 0 ? p1 : p2;
            float x = (i == 0 ? -1f : 1f) * sv.x * 0.24f;
            shScore[i] = ShText($"Score{i + 1}", layerShop, "", p.color, sv.x / 30f, new Vector2(x, 0f));
            shInfo[i] = ShText($"Info{i + 1}", layerShop, "", Color.white, sv.x / 80f, new Vector2(x, -sv.y * 0.14f));
        }
        // 2人の点数の割合のバー
        shBarBack = ShSprite("BarBack", layerShop, GetSquareSprite(), 10);
        shBar1 = ShSprite("Bar1", layerShop, GetSquareSprite(), 11);
        shBar2 = ShSprite("Bar2", layerShop, GetSquareSprite(), 11);
        shBarBack.color = new Color(0.2f, 0.2f, 0.25f);
        shBar1.color = p1.color;
        shBar2.color = p2.color;
        shCenter = ShText("Center", layerShop, "", Color.white, sv.x / 40f, new Vector2(0f, -sv.y * 0.36f));
        LayoutShooterShop();
    }

    SpriteRenderer ShSprite(string n, int layer, Sprite sp, int order)
    {
        var sr = new GameObject(n).AddComponent<SpriteRenderer>();
        sr.transform.SetParent(shooterRoot, false);
        sr.gameObject.layer = layer;
        sr.sprite = sp;
        sr.sortingOrder = order;
        return sr;
    }

    TextMesh ShText(string n, int layer, string text, Color color, float size, Vector2 pos)
    {
        var tm = MakeText(n, shooterRoot, layer, text, color, 470);
        tm.fontStyle = FontStyle.Bold;
        SetTextSize(tm, size);
        tm.transform.position = ShooterOrigin + (Vector3)pos;
        return tm;
    }

    // ---------- 毎フレーム(Update から) ----------

    void UpdateShooter()
    {
        if (shooters == null) return;
        float dt = Time.deltaTime;
        if (!shooterEnded)
        {
            shooterLeft = Mathf.Max(0f, shooterLeft - dt);
            if (shooterLeft <= 0f) EndShooter();
        }
        foreach (var s in shooters) StepShooter(s, dt);
        StepShPops(dt);
        RefreshShooterShop();

        if (shooterEnded && Time.time - shooterEndTime >= Mathf.Max(0.5f, resultRestartDelay) && AnyAPressed())
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    void StepShooter(Shooter s, float dt)
    {
        Vector2 pv = PlayerViewSize();
        float unit = Mathf.Min(pv.x, pv.y);
        bool playing = shooterOn && !shooterEnded;
        bool canAim = !shooterEnded;   // エントリー画面でも照準を動かせる

        // 照準を動かす
        Vector2 move = canAim ? (s.index == 0 ? GetMove1P() : GetMove2P()) : Vector2.zero;
        if (move.magnitude > 1f) move.Normalize();
        s.aim += move * unit * Mathf.Max(0.1f, shooterAimSpeed) * dt;
        s.aim.x = Mathf.Clamp(s.aim.x, -pv.x * 0.47f, pv.x * 0.47f);
        s.aim.y = Mathf.Clamp(s.aim.y, -pv.y * 0.3f, pv.y * 0.45f);

        // A を押している = 顔を出す(撃たれて動けないあいだは、かくれたまま)
        s.stun = Mathf.Max(0f, s.stun - dt);
        s.peek = canAim && s.stun <= 0f && ShootHeld(s.index);
        s.coverK = Mathf.MoveTowards(s.coverK, s.peek ? 0f : 1f, dt * 7f);

        // 撃つ / リロード
        s.fireCool -= dt;
        int maxAmmo = Mathf.Max(1, shooterAmmo);
        if (s.peek)
        {
            s.reload = 0f;
            if (playing && s.ammo > 0 && s.fireCool <= 0f && s.coverK < 0.3f)
            {
                s.fireCool = Mathf.Max(0.03f, shooterFireRate);
                s.ammo--;
                ShFire(s, unit);
            }
        }
        else if (s.ammo < maxAmmo && playing)
        {
            s.reload += dt;
            if (s.reload >= Mathf.Max(0.1f, shooterReloadTime)) { s.ammo = maxAmmo; s.reload = 0f; ShPopup(s.layer, "RELOAD OK", s.aim + Vector2.up * unit * 0.08f, new Color(0.3f, 1f, 1f), unit / 70f, 0.6f); }
        }

        // 的を出す(時間がたつほど早く出る)
        if (playing)
        {
            s.nextSpawn -= dt;
            float elapsed = Mathf.Max(5f, shooterTime) - shooterLeft;
            float rate = Mathf.Lerp(1f, 0.5f, Mathf.Clamp01(elapsed / Mathf.Max(5f, shooterTime)));
            if (s.nextSpawn <= 0f && s.targets.Count < Mathf.Max(1, shooterMaxTargets))
            {
                ShSpawnTarget(s, pv);
                s.nextSpawn = Mathf.Max(0.2f, shooterSpawnInterval) * rate * Random.Range(0.7f, 1.3f);
            }
        }

        // 的を動かす・撃ってくる
        for (int i = s.targets.Count - 1; i >= 0; i--)
        {
            var t = s.targets[i];
            if (ShStepTarget(s, t, pv, dt, playing)) continue;
            Destroy(t.tr.gameObject);
            s.targets.RemoveAt(i);
        }

        // 見た目
        s.cross.position = ShooterOrigin + (Vector3)s.aim + new Vector3(0f, 0f, -1f);
        s.crossSr.color = s.stun > 0f ? new Color(1f, 0.2f, 0.2f, 0.6f)
                        : s.peek ? (s.ammo > 0 ? new Color(1f, 1f, 1f, 1f) : new Color(1f, 0.5f, 0.2f, 0.8f))
                        : new Color(1f, 1f, 1f, 0.35f);
        float coverTop = Mathf.Lerp(-pv.y * 0.36f, -pv.y * 0.05f, Mathf.SmoothStep(0f, 1f, s.coverK));
        float coverH = coverTop + pv.y / 2f;
        Vector2 cvs = s.cover.sprite.bounds.size;
        s.cover.transform.localScale = new Vector3(pv.x * 1.02f / cvs.x, Mathf.Max(0.01f, coverH) / cvs.y, 1f);
        s.cover.transform.position = ShooterOrigin + new Vector3(0f, coverTop, -0.5f);   // ピボットは上のふち

        s.damage = Mathf.MoveTowards(s.damage, 0f, dt * 2.5f);
        s.redFlash.color = new Color(1f, 0f, 0.1f, s.damage * 0.45f);
        s.shake = Mathf.MoveTowards(s.shake, 0f, dt * 3f);
        s.glitch.root.position = Vector3.zero;
        StepGlitch(s.glitch, s.damage, Time.time);
        s.glitch.root.position = ShooterOrigin;

        if (!playing) return;
        string ammoBar = new string('|', s.ammo) + new string('.', maxAmmo - s.ammo);
        s.state.text = s.stun > 0f ? "やられた！"
                     : s.peek ? (s.ammo > 0 ? ammoBar : "弾切れ！ Aを離してかくれる")
                     : s.ammo < maxAmmo ? $"リロード中… {Mathf.RoundToInt(100f * s.reload / Mathf.Max(0.1f, shooterReloadTime))}%"
                     : "Aを押すと 顔を出して撃つ";
        s.state.color = s.stun > 0f ? new Color(1f, 0.3f, 0.3f) : s.peek ? new Color(0.3f, 1f, 1f) : Color.white;
        Fit(s.state, pv.x * 0.9f, pv.y * 0.1f, unit / 22f);
        s.state.transform.position = ShooterOrigin + new Vector3(0f, -pv.y * 0.42f, -2f);
    }

    bool ShootHeld(int i)
    {
        var pad = PadOf(i);
        if (pad != null && pad.buttonSouth.isPressed) return true;
        var k = Kb;
        if (k == null) return false;
        return i == 0 ? k.spaceKey.isPressed : (k.enterKey.isPressed || k.numpadEnterKey.isPressed);
    }

    void ShFire(Shooter s, float unit)
    {
        ShPlay(GetShShotClip(), 0.5f);
        // いちばん手前(後から出た)の、照準の中にある的に当たる
        ShTarget hit = null;
        for (int i = s.targets.Count - 1; i >= 0; i--)
        {
            var t = s.targets[i];
            if (t.leaving || t.appear < 0.5f) continue;
            Vector2 d = s.aim - t.pos;
            if (Mathf.Abs(d.x) < t.size * 0.35f && Mathf.Abs(d.y) < t.size * 0.5f) { hit = t; break; }
        }
        // 反動(照準が少し上にぶれる)
        s.aim += new Vector2(Random.Range(-1f, 1f) * unit * 0.006f, unit * 0.008f);
        if (hit == null)
        {
            ShPopup(s.layer, "*", s.aim, new Color(1f, 0.9f, 0.4f), unit / 40f, 0.15f);
            return;
        }
        hit.hp--;
        hit.hitFlash = 0.08f;
        s.score += 10;
        if (hit.hp <= 0)
        {
            s.score += 100;
            hit.leaving = true;
            hit.life = 0f;
            ShPlay(GetShBreakClip(), 0.6f);
            ShPopup(s.layer, "+100", hit.pos + Vector2.up * hit.size * 0.4f, new Color(1f, 0.9f, 0.2f), unit / 35f, 0.8f);
        }
        else
        {
            ShPlay(GetShHitClip(), 0.35f);
            ShPopup(s.layer, "+10", s.aim + new Vector2(Random.Range(-1f, 1f), 1f) * unit * 0.03f, new Color(0.4f, 1f, 1f), unit / 60f, 0.4f);
        }
    }

    void ShSpawnTarget(Shooter s, Vector2 pv)
    {
        float unit = Mathf.Min(pv.x, pv.y);
        var t = new ShTarget();
        t.size = unit * Mathf.Max(0.05f, shooterTargetSize) * Random.Range(0.8f, 1.2f);
        // ほかの的と重なりにくい場所を探す
        for (int tries = 0; tries < 8; tries++)
        {
            t.pos = new Vector2(Random.Range(-pv.x * 0.4f, pv.x * 0.4f), Random.Range(-pv.y * 0.02f, pv.y * 0.36f));
            bool ok = true;
            foreach (var o in s.targets) if (Vector2.Distance(o.pos, t.pos) < (o.size + t.size) * 0.5f) { ok = false; break; }
            if (ok) break;
        }
        if (Random.value < 0.4f) t.vel = new Vector2((Random.value < 0.5f ? -1f : 1f) * unit * Random.Range(0.08f, 0.2f), 0f);
        t.hp = Mathf.Max(1, shooterTargetHp + Random.Range(-1, 2));
        t.attackIn = Random.Range(2f, 4f);
        t.life = Mathf.Max(2f, shooterTargetLife);

        var go = new GameObject("Target");
        go.transform.SetParent(shooterRoot, false);
        go.layer = s.layer;
        t.tr = go.transform;
        t.sr = go.AddComponent<SpriteRenderer>();
        t.sr.sprite = shooterTargetSprite != null ? shooterTargetSprite : GetShTargetSprite();
        t.sr.sortingOrder = 100 + s.targets.Count;
        t.ring = new GameObject("Warn").AddComponent<SpriteRenderer>();
        t.ring.transform.SetParent(t.tr, false);
        t.ring.gameObject.layer = s.layer;
        t.ring.sprite = GetShRingSprite();
        t.ring.sortingOrder = 200;
        t.mark = MakeText("Mark", t.tr, s.layer, "!", new Color(1f, 0.2f, 0.2f), 201);
        t.mark.fontStyle = FontStyle.Bold;
        SetTextSize(t.mark, unit / 25f);
        s.targets.Add(t);
    }

    // 的を1フレームぶん動かす。消すときは false
    bool ShStepTarget(Shooter s, ShTarget t, Vector2 pv, float dt, bool playing)
    {
        float unit = Mathf.Min(pv.x, pv.y);
        if (t.leaving)
        {
            t.appear -= dt * 4f;
            if (t.appear <= 0f) return false;
        }
        else
        {
            t.appear = Mathf.Min(1f, t.appear + dt * 3f);
            if (playing)
            {
                t.life -= dt;
                if (t.life <= 0f) t.leaving = true;
                t.pos += t.vel * dt;
                if (Mathf.Abs(t.pos.x) > pv.x * 0.42f) { t.vel.x = -t.vel.x; t.pos.x = Mathf.Clamp(t.pos.x, -pv.x * 0.42f, pv.x * 0.42f); }
                // 撃ってくる
                t.attackIn -= dt;
                if (t.attackIn <= 0f)
                {
                    t.attackIn = Random.Range(2.5f, 4.5f);
                    if (s.peek) ShDamaged(s, unit);
                    else ShPopup(s.layer, "ガード！", new Vector2(t.pos.x, -pv.y * 0.12f), new Color(0.4f, 0.8f, 1f), unit / 45f, 0.6f);
                }
            }
        }
        // 見た目: 下からせり上がる / 当たると白く光る
        Vector2 ss = t.sr.sprite.bounds.size;
        float k = t.size / Mathf.Max(0.01f, ss.y);
        float pop = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t.appear));
        t.tr.localScale = new Vector3(k, k * pop, 1f);
        t.tr.position = ShooterOrigin + new Vector3(t.pos.x, t.pos.y - t.size * 0.5f * (1f - pop), 0f);
        t.hitFlash -= dt;
        t.sr.color = t.hitFlash > 0f ? new Color(1f, 0.55f, 0.55f, 1f) : Color.white;
        // 撃ってくる前の赤い輪(縮んでいく)と「!」
        float warn = Mathf.Max(0.2f, shooterAttackWarn);
        bool warning = playing && !t.leaving && t.attackIn < warn;
        t.ring.enabled = warning;
        t.mark.gameObject.SetActive(warning && Mathf.Repeat(Time.time * 8f, 1f) < 0.6f);
        if (warning)
        {
            float r = Mathf.Lerp(1f, 2.2f, t.attackIn / warn);   // 的の大きさの何倍か
            Vector2 rs = t.ring.sprite.bounds.size;
            float worldR = t.size * r;
            // 親(的)の大きさで割って、輪は丸いまま
            t.ring.transform.localScale = new Vector3(worldR / rs.x / Mathf.Max(0.0001f, k), worldR / rs.y / Mathf.Max(0.0001f, k * pop), 1f);
            t.ring.color = new Color(1f, 0.15f, 0.2f, Mathf.Lerp(1f, 0.4f, t.attackIn / warn));
            t.mark.transform.position = t.tr.position + new Vector3(0f, t.size * 0.75f, -0.1f);
            t.mark.transform.localScale = new Vector3(1f / Mathf.Max(0.0001f, k), 1f / Mathf.Max(0.0001f, k * pop), 1f);
        }
        return true;
    }

    void ShDamaged(Shooter s, float unit)
    {
        s.score = Mathf.Max(0, s.score - Mathf.Max(0, shooterHitPenalty));
        s.stun = Mathf.Max(0.1f, shooterStunTime);
        s.damage = 1f;
        s.shake = 1f;
        ShPlay(GetShDamageClip(), 0.7f);
        ShPopup(s.layer, $"-{shooterHitPenalty}", s.aim, new Color(1f, 0.25f, 0.25f), unit / 30f, 0.8f);
    }

    void EndShooter()
    {
        shooterEnded = true;
        shooterEndTime = Time.time;
        int a = shooters[0].score, b = shooters[1].score;
        for (int i = 0; i < 2; i++)
        {
            var s = shooters[i];
            int mine = s.score, other = shooters[1 - i].score;
            s.peek = false;
            foreach (var t in s.targets) t.leaving = true;
            s.state.text = mine > other ? "WIN!" : mine < other ? "LOSE…" : "DRAW";
            s.state.color = mine > other ? new Color(1f, 0.85f, 0.2f) : mine < other ? new Color(0.6f, 0.6f, 0.8f) : Color.white;
            Vector2 pv = PlayerViewSize();
            Fit(s.state, pv.x * 0.8f, pv.y * 0.3f, Mathf.Min(pv.x, pv.y) / 6f);
            s.state.transform.position = ShooterOrigin + new Vector3(0f, pv.y * 0.1f, -2f);
            s.state.GetComponent<MeshRenderer>().sortingOrder = 480;
        }
        shCenter.text = a > b ? $"{p1.name} の勝ち！" : a < b ? $"{p2.name} の勝ち！" : "引き分け！";
        shCenter.color = a > b ? p1.color : a < b ? p2.color : Color.white;
    }

    void RefreshShooterShop()
    {
        int maxAmmo = Mathf.Max(1, shooterAmmo);
        if (shooterOn)
        {
            int sec = Mathf.CeilToInt(shooterLeft);
            shTimer.text = shooterEnded ? "TIME UP" : $"{sec / 60}:{sec % 60:00}";
            shTimer.color = shooterEnded || shooterLeft > 10f ? new Color(0.3f, 1f, 1f) : new Color(1f, 0.35f, 0.3f);
        }
        for (int i = 0; i < 2; i++)
        {
            var s = shooters[i];
            shScore[i].text = $"{s.p.name}\n{s.score}";
            shInfo[i].text = !shooterOn ? (entryReady[i] ? "参加OK！" : "Aで参加")
                           : shooterEnded ? ""
                           : s.stun > 0f ? "やられた！"
                           : (s.peek ? "撃っている" : "かくれている") + $"\n弾 {s.ammo}/{maxAmmo}";
        }
        if (shooterEnded && Time.time - shooterEndTime >= Mathf.Max(0.5f, resultRestartDelay) && !shCenter.text.Contains("\n"))
            shCenter.text += "\nAでもう一度";
        LayoutShooterShop();
    }

    // 真ん中の画面の並び(上から: 名前 / 時間 / 1P・2P の点数 / 割合のバー / 勝ち負けなど)。文字は画面からはみ出さない大きさにする
    void LayoutShooterShop()
    {
        if (shooters == null) return;
        Vector2 sv = ShopViewSize();
        float m = Mathf.Min(sv.x, sv.y);
        void Put(TextMesh tm, float x, float y, float w, float h, float cap)
        {
            Fit(tm, w, h, cap);
            tm.transform.position = ShooterOrigin + new Vector3(x, y, 0f);
        }
        Put(shTitle, 0f, sv.y * 0.39f, sv.x * 0.9f, sv.y * 0.1f, m / 12f);
        Put(shTimer, 0f, sv.y * 0.24f, sv.x * 0.7f, sv.y * 0.16f, m / 5f);
        for (int i = 0; i < 2; i++)
        {
            float x = (i == 0 ? -1f : 1f) * sv.x * 0.25f;
            Put(shScore[i], x, sv.y * 0.04f, sv.x * 0.44f, sv.y * 0.2f, m / 8f);
            Put(shInfo[i], x, -sv.y * 0.12f, sv.x * 0.44f, sv.y * 0.1f, m / 22f);
        }
        Put(shCenter, 0f, -sv.y * 0.36f, sv.x * 0.9f, sv.y * 0.16f, m / 10f);

        // 点数の割合のバー(左 1P / 右 2P)
        float total = shooters[0].score + shooters[1].score;
        float share = total > 0f ? shooters[0].score / total : 0.5f;
        float w = sv.x * 0.85f, h = sv.y * 0.03f, y = -sv.y * 0.23f;
        Vector2 qs = GetSquareSprite().bounds.size;
        shBarBack.transform.localScale = new Vector3((w + h * 0.4f) / qs.x, h * 1.4f / qs.y, 1f);
        shBarBack.transform.position = ShooterOrigin + new Vector3(0f, y, 0f);
        float w1 = w * share, w2 = w - w1;
        shBar1.transform.localScale = new Vector3(Mathf.Max(0.0001f, w1) / qs.x, h / qs.y, 1f);
        shBar1.transform.position = ShooterOrigin + new Vector3(-w / 2f + w1 / 2f, y, 0f);
        shBar2.transform.localScale = new Vector3(Mathf.Max(0.0001f, w2) / qs.x, h / qs.y, 1f);
        shBar2.transform.position = ShooterOrigin + new Vector3(w / 2f - w2 / 2f, y, 0f);
    }

    // ---------- 浮かんで消える文字 ----------

    void ShPopup(int layer, string text, Vector2 pos, Color color, float size, float life)
    {
        if (shooterRoot == null) return;
        var tm = MakeText("Pop", shooterRoot, layer, text, color, 490);
        tm.fontStyle = FontStyle.Bold;
        Vector2 v = layer == layerShop ? ShopViewSize() : PlayerViewSize();
        size = Mathf.Min(size, FitSize(text, v.x * 0.9f, v.y * 0.5f, size));
        SetTextSize(tm, size);
        tm.transform.position = ShooterOrigin + new Vector3(pos.x, pos.y, -2f);
        shPops.Add(new ShPop { tm = tm, life = Mathf.Max(0.05f, life), vel = new Vector3(0f, size * 6f, 0f) });
    }

    void StepShPops(float dt)
    {
        for (int i = shPops.Count - 1; i >= 0; i--)
        {
            var p = shPops[i];
            p.t += dt;
            if (p.t >= p.life || p.tm == null)
            {
                if (p.tm != null) Destroy(p.tm.gameObject);
                shPops.RemoveAt(i);
                continue;
            }
            p.tm.transform.position += p.vel * dt;
            var c = p.tm.color;
            c.a = 1f - Mathf.Pow(p.t / p.life, 3f);
            p.tm.color = c;
        }
    }

    // ---------- カメラ(LateUpdate のいちばん最後。詰め放題のカメラの位置を上書きして、射撃場を映す) ----------

    void UpdateShooterCameras()
    {
        if (shooterRoot == null || shooters == null || switcher == null || switcher.screens == null) return;
        foreach (var s in shooters)
        {
            if (s.p.screen < 0 || s.p.screen >= switcher.screens.Length) continue;
            var cam = switcher.screens[s.p.screen].camera;
            if (cam == null) continue;
            if (!shCamBase.ContainsKey(s.p.screen)) shCamBase[s.p.screen] = cam.transform.position;
            Vector2 pv = PlayerViewSize();
            float amp = Mathf.Min(pv.x, pv.y) * 0.02f * s.shake;
            Vector3 shake = new Vector3(Random.Range(-1f, 1f), Random.Range(-1f, 1f), 0f) * amp;
            cam.transform.position = new Vector3(ShooterOrigin.x, ShooterOrigin.y, cam.transform.position.z) + shake;
        }
        if (screenShop >= 0 && screenShop < switcher.screens.Length)
        {
            var cam = switcher.screens[screenShop].camera;
            if (cam != null)
            {
                cam.transform.rotation = Quaternion.Euler(0f, 0f, shopScreenAngle);   // 棚の傾け(shopCameraAngle)は使わない
                cam.transform.position = ShooterOrigin + new Vector3(0f, 0f, -10f);
            }
        }
    }

    // ---------- 音(スクリプトで作る短い音) ----------

    void ShPlay(AudioClip clip, float volume)
    {
        if (!shooterSound || shAudio == null || clip == null) return;
        shAudio.PlayOneShot(clip, volume * shooterVolume);
    }

    static AudioClip MakeShClip(string n, float seconds, System.Func<float, System.Random, float> wave)
    {
        int rate = 44100, count = Mathf.Max(1, (int)(rate * seconds));
        var data = new float[count];
        var rnd = new System.Random(n.GetHashCode());
        for (int i = 0; i < count; i++) data[i] = Mathf.Clamp(wave(i / (float)rate, rnd), -1f, 1f);
        var clip = AudioClip.Create(n, count, 1, rate, false);
        clip.SetData(data, 0);
        return clip;
    }

    static float Noise(System.Random r) => (float)(r.NextDouble() * 2.0 - 1.0);

    // 撃つ音: 短い「パン」
    static AudioClip GetShShotClip() => shShotClip != null ? shShotClip :
        shShotClip = MakeShClip("ShShot", 0.09f, (t, r) => (Noise(r) * 0.7f + Mathf.Sin(t * 2f * Mathf.PI * 180f) * 0.5f) * Mathf.Exp(-t * 45f));
    // 当たった音: 高い「キン」
    static AudioClip GetShHitClip() => shHitClip != null ? shHitClip :
        shHitClip = MakeShClip("ShHit", 0.12f, (t, r) => Mathf.Sin(t * 2f * Mathf.PI * 1500f) * Mathf.Exp(-t * 35f) * 0.6f);
    // こわした音: 「ガシャン」
    static AudioClip GetShBreakClip() => shBreakClip != null ? shBreakClip :
        shBreakClip = MakeShClip("ShBreak", 0.35f, (t, r) => (Noise(r) * 0.8f + Mathf.Sin(t * 2f * Mathf.PI * 90f) * 0.6f) * Mathf.Exp(-t * 10f));
    // 撃たれた音: 低い「ドン」
    static AudioClip GetShDamageClip() => shDamageClip != null ? shDamageClip :
        shDamageClip = MakeShClip("ShDamage", 0.3f, (t, r) => (Mathf.Sin(t * 2f * Mathf.PI * Mathf.Lerp(120f, 40f, t / 0.3f)) * 0.9f + Noise(r) * 0.3f) * Mathf.Exp(-t * 9f));

    // ---------- 絵(スクリプトで描く) ----------

    // 的: 積み重なった四角いロボット(下ほど大きい3段。いちばん上に赤く光る目。ネオンのふち)
    static Sprite GetShTargetSprite()
    {
        if (shTargetSprite != null) return shTargetSprite;
        const int w = 64, h = 96;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        var px = new Color[w * h];
        Color body = new Color(0.12f, 0.1f, 0.2f), pink = new Color(1f, 0.2f, 0.8f), cyan = new Color(0.2f, 1f, 1f);
        // (左, 下, 幅, 高さ, ふちの色)
        int[,] boxes = { { 6, 2, 52, 34 }, { 12, 38, 40, 28 }, { 18, 68, 28, 26 } };
        for (int b = 0; b < 3; b++)
        {
            int bx = boxes[b, 0], by = boxes[b, 1], bw = boxes[b, 2], bh = boxes[b, 3];
            Color edge = b == 1 ? pink : cyan;
            for (int y = by; y < by + bh; y++)
                for (int x = bx; x < bx + bw; x++)
                {
                    bool border = x < bx + 2 || x >= bx + bw - 2 || y < by + 2 || y >= by + bh - 2;
                    bool corner = (x - bx < 3 || bx + bw - 1 - x < 3) && (y - by < 3 || by + bh - 1 - y < 3);
                    if (corner) continue;
                    Color c = border ? edge : body;
                    // 真ん中に横の光る線
                    if (!border && Mathf.Abs(y - (by + bh / 2)) < 1 && x > bx + 6 && x < bx + bw - 6) c = edge * 0.7f;
                    c.a = 1f;
                    px[y * w + x] = c;
                }
        }
        // 目(いちばん上の段の真ん中。赤く光る)
        for (int y = 76; y < 86; y++)
            for (int x = 24; x < 40; x++)
            {
                float dx = (x - 31.5f) / 8f, dy = (y - 80.5f) / 5f;
                if (dx * dx + dy * dy <= 1f) px[y * w + x] = dx * dx + dy * dy < 0.3f ? new Color(1f, 0.9f, 0.9f) : new Color(1f, 0.1f, 0.2f);
            }
        tex.SetPixels(px);
        tex.Apply();
        shTargetSprite = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), h);
        return shTargetSprite;
    }

    // 照準: 輪と4本の線と真ん中の点
    static Sprite GetShCrossSprite()
    {
        if (shCrossSprite != null) return shCrossSprite;
        const int n = 64;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color[n * n];
        float c = n / 2f - 0.5f;
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float dx = x - c, dy = y - c, d = Mathf.Sqrt(dx * dx + dy * dy);
                bool ring = Mathf.Abs(d - 22f) < 1.6f;
                bool tick = (Mathf.Abs(dx) < 1.2f && Mathf.Abs(dy) > 12f && Mathf.Abs(dy) < 31f) || (Mathf.Abs(dy) < 1.2f && Mathf.Abs(dx) > 12f && Mathf.Abs(dx) < 31f);
                bool dot = d < 2.2f;
                px[y * n + x] = ring || tick || dot ? Color.white : Color.clear;
            }
        tex.SetPixels(px);
        tex.Apply();
        shCrossSprite = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), n);
        return shCrossSprite;
    }

    // 撃ってくる前の輪(ふちだけ)
    static Sprite GetShRingSprite()
    {
        if (shRingSprite != null) return shRingSprite;
        const int n = 64;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color[n * n];
        float c = n / 2f - 0.5f;
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
                px[y * n + x] = Mathf.Abs(d - 29f) < 2f ? Color.white : Color.clear;
            }
        tex.SetPixels(px);
        tex.Apply();
        shRingSprite = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), n);
        return shRingSprite;
    }

    // かくれる壁: 暗い金属に、上のふちがネオンで光り、黄色と黒のしま(ピボットは上のふちの真ん中)
    static Sprite GetShCoverSprite()
    {
        if (shCoverSprite != null) return shCoverSprite;
        const int w = 128, h = 64;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                Color c = new Color(0.1f, 0.09f, 0.14f);
                if (y >= h - 3) c = new Color(0.2f, 1f, 1f);                                   // 上のふち(光る)
                else if (y >= h - 12 && y < h - 5) c = ((x + y) / 6) % 2 == 0 ? new Color(1f, 0.8f, 0.1f) : new Color(0.05f, 0.05f, 0.05f);   // しま
                else if (x % 32 == 0 || y % 20 == 0) c = new Color(0.18f, 0.16f, 0.24f);          // 板のつなぎ目
                c.a = 1f;
                px[y * w + x] = c;
            }
        tex.SetPixels(px);
        tex.Apply();
        shCoverSprite = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 1f), h);
        return shCoverSprite;
    }
}
