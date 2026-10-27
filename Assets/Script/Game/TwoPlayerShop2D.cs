using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 2人プレイ用: 商品棚(画面2)で1P/2Pがカーソルで商品を掴むと、
/// 商品とカーソルがそのプレイヤーの画面(1P=画面1 / 2P=画面3)へ移る。
/// 自分の画面では横にだけ動ける(高さは dropHeight で固定)。
/// Aボタンを押すと商品を離し、Rigidbody2Dで自由落下する。
/// 離した後、カーソルは商品棚へ戻る。
/// 1P/2Pの画面にはレジ袋があり、商品が入った瞬間にその商品の金額が出て、袋の合計に足される。
/// 袋の外に落ちた商品(床に着いた / 袋の外で止まった)は消える。
/// 1人が商品を掴んでから grabWindow 秒以内に、もう1人が同じ商品を掴むと「掴み合い」になり、
/// 棚の画面にゲージが出る。ゲージが最大に近いときにAで止めた方が商品を勝ち取る(チャージインパクト風)。
/// 2人ともAを押すまで掴み合いは終わらない。引き分け(表示の数字が同じ)ならやり直す。
/// コントローラーは「最初にAボタンを押した方が1P、次が2P」で割り当てる。
/// キーボードは予備(1P: WASD+Space / 2P: 矢印+Enter)。
/// 棚の商品は取られると、少し待ってから同じ場所に同じ商品が補充される。
/// DisplayTestSwitcher と併用する。
///
/// 棚の画面(画面2)は 横 / 縦 / 縦(モニターだけ回す) を shopOrientation で切り替えられる。
/// ファイルの分け方(すべて同じ1つのスクリプト。partial class):
///   TwoPlayerShop2D.cs        設定の欄・初期化・棚・移動・掴む/離す・画面表示
///   TwoPlayerShop2D.Clash.cs  掴み合い
///   TwoPlayerShop2D.Bag.cs    レジ袋・金額・袋の外の商品を消す
///   TwoPlayerShop2D.Sprites.cs 商品の図形(四角・丸・三角・多角形・L字など)・矢印・ゲージの形の自動生成
///   TwoPlayerShop2D.Entry.cs  エントリー画面(A で参加 → 2人そろったらカウントダウン → 開始)
///   TwoPlayerShop2D.Pads.cs   コントローラーの 1P/2P の登録(F2。パソコンに保存して、次からも同じ側にする)
///   TwoPlayerShop2D.Input.cs  コントローラー/キーボードの入力
///   TwoPlayerShop2D.ShopView.cs 棚の画面の向き(横/縦)・カメラ
///   TwoPlayerShop2D.Shelf.cs  棚(段と列・商品を置く場所・仮の棚/画像/3Dモデルの見た目)
///   TwoPlayerShop2D.Fragile.cs 割れる商品(上に重さが乗ると割れて袋から消える)
///   TwoPlayerShop2D.Creak.cs  袋がきしむ擬音(重くなってきたら袋のまわりに「ミシ…」)
///   TwoPlayerShop2D.Menu.cs   献立ミッション(食材5品がそろったら、それ以降の商品が1.2倍)
///   TwoPlayerShop2D.Deck.cs   ランダム補充の山札(引く・戻す)
///   TwoPlayerShop2D.Products.cs 商品の一覧(ProductInfo。名前・ランク・金額・画像・3Dモデル)・3Dモデルの商品・値札
///   TwoPlayerShop2D.PlayerView.cs 1P/2Pの画面の大きさ(どんな形の画面でも範囲が全部映る)・床/袋の配置
///   TwoPlayerShop2D.Monitor.cs 縦型モニター用 / 横型モニター用の配置の切り替え
///   TwoPlayerShop2D.Screens.cs 1P/棚/2P をどのカメラ(ディスプレイ)に映すかの割り当て・入れ替え
///   TwoPlayerShop2D.Result.cs タイムアップのあとの勝ち負けの表示・もう一度
///   TwoPlayerShop2D.Weight.cs 商品の重さ(仮の重さ)・最後に持ち上げたとき重すぎたら袋が破れる(0円)
///   TwoPlayerShop2D.Lift.cs   タイムアップのあと、結果発表の前にレジ袋を持ち上げる(こぼれた商品は合計から引く)
///   TwoPlayerShop2D.Kick.cs   キック(LBでゲージ → Aで威力 → 足が飛んで相手のレジ袋が揺れる)
///   TwoPlayerShop2D.Timer.cs  制限時間(棚の画面に残り時間を出す)
///   Inspector の欄の順番が変わらないように、public の設定はすべて本体に置く
/// </summary>
public partial class TwoPlayerShop2D : MonoBehaviour
{
    [Header("参照")]
    public DisplayTestSwitcher switcher;

    [Header("文字(フォント)。再生する前に決める")]
    [Tooltip("ゲーム中の文字のフォント(.ttf / .otf を Assets に入れて、ここへドラッグ)。日本語が入っているフォントにする。空なら Unity の標準フォント")]
    public Font textFont;
    [Tooltip("文字の解像度(1〜4)。大きいほどくっきりする(見た目の大きさは変わらない)。2 で前の2倍")]
    [Range(1, 4)] public int textResolution = 2;

    [Header("モニターの置き方(縦型モニター用 / 横型モニター用を切り替える)")]
    [Tooltip("縦型モニター: 本番 / 横型(縦と同じ見た目): 横のモニターの真ん中に本番と同じ縦長の画面を出す / 横型(横いっぱい): 横のモニターに合わせて並べ直す。再生中に変えてもすぐ反映(商品・カーソルの大きさは新しく出たものから)")]
    public MonitorMode monitorMode = MonitorMode.Portrait;
    [Tooltip("縦型モニターのとき、棚の画面を90度回して描くか(Windows は横のまま棚のモニターだけ倒したとき用。逆さならもう一方)。1P/2P は「1P/2Pの画面」の P1/P2 Screen Rotation")]
    public ScreenRotation shopScreenRotation = ScreenRotation.None;
    [Tooltip("縦の配置(縦型モニター・横型(縦と同じ見た目)で使う)")]
    public LayoutProfile portraitLayout = LayoutProfile.PortraitDefault();
    [Tooltip("横の配置(横型(横いっぱい)で使う)")]
    public LayoutProfile landscapeLayout = LayoutProfile.LandscapeDefault();

    [Header("機能のON/OFF(チェックを外すとその機能が止まる。★は再生前に決める)")]
    [Tooltip("棚: 取られた商品を同じ場所に補充する")]
    public bool refillShelf = true;
    [Tooltip("自分の画面: 商品を持っている間は横にだけ動く。OFFなら上下にも動ける")]
    public bool horizontalOnlyOnOwnScreen = true;
    [Tooltip("自分の画面: 商品を持っている間、RB で90度ずつ回せる(キーボード予備: 1P E / 2P 右Ctrl)")]
    public bool rotateHeldProduct = true;
    [Tooltip("★自分の画面: 床と左右の壁を作る")]
    public bool createFloor = true;
    [Tooltip("★自分の画面: レジ袋を置く(合計金額も袋といっしょに出る)")]
    public bool createBag = true;
    [Tooltip("献立ミッション: 献立の食材5品が全部自分の袋に入ったら「○○完成！」。それ以降に袋に入れた商品の金額が Menu Bonus Rate 倍")]
    public bool menuMission = true;
    [Tooltip("レジ袋: 袋の上に合計金額を出す")]
    public bool showBagTotal = true;
    [Tooltip("レジ袋: 商品が入ったときに「+¥金額」を出す")]
    public bool showPricePopup = true;
    [Tooltip("レジ袋: 袋の外に落ちた商品を消す(袋に入らなかったもの・袋から場外に落ちたもの)")]
    public bool removeOutsideBag = true;
    [Tooltip("レジ袋: 割れる商品(一覧の Fragile。たまご など)の上に重さが乗ると、割れて袋から消え、金額を合計から引く")]
    public bool breakFragile = true;
    [Tooltip("レジ袋: 袋から場外に落ちて消えた商品の金額を、合計から引く")]
    public bool subtractSpilledFromTotal = true;
    [Tooltip("タイムアップ: 最後に袋を持ち上げたとき、袋の中の商品の重さの合計が Bag Max Weight を超えていたら袋の底が破れて、中身が落ちて散らばり、その人の金額は0円になる(Bag Lift On Time Up が ON のとき)")]
    public bool bagTearEnabled = true;
    [Tooltip("レジ袋: 袋の中が重くなってきたら(破れる重さに近づいたら)、袋のまわりに「ミシ…」などの擬音を出す。重いほどたくさん出る")]
    public bool bagCreakEnabled = true;
    [Tooltip("商品の重さ: 仮の重さを使う(名前から決めた適当な値。飲み物・魚・肉は重く、お菓子は軽い)。OFF なら商品の一覧の Weight を使う")]
    public bool useTempWeights = true;
    [Tooltip("掴み合い: 2人が同じ商品を続けて掴んだら掴み合いになる")]
    public bool clashEnabled = true;
    [Tooltip("掴み合い: 引き分けならやり直す。OFFならランダムで勝ちを決める")]
    public bool clashRematchOnTie = true;
    [Tooltip("掴み合い: ゲージを、画面を大きく使う斜めの形にする(左下から1P・右上から2Pが真ん中のVSへ伸びる)。OFFなら横に2本の形")]
    public bool clashGaugeDiagonal = true;
    [Tooltip("掴み合い: 横に2本の形のとき、ゲージを棚の画面の真ん中に出す(連打式でもタイミング式でも効く)")]
    public bool clashGaugeCentered = true;
    [Tooltip("掴み合い: 決着したら、勝った人の画面の真ん中に「勝ち」、負けた人の画面の真ん中に「負け」を大きく出す(秒数は Clash Win Lose Time)")]
    public bool showClashWinLose = true;
    [Tooltip("キック: 棚を操作中(商品を持っていないとき)にLBでキックできる")]
    public bool kickEnabled = true;
    [Tooltip("キック: 足が届いたら相手のレジ袋を揺らす")]
    public bool kickShakeBag = true;
    [Tooltip("キック: 足が届いたら1P/2Pの画面を揺らす")]
    public bool kickShakeScreen = true;
    [Tooltip("キック: 蹴った人のレジ袋・画面も揺れる(強さは Kick Self Shake)")]
    public bool kickShakeSelf = true;
    [Tooltip("1P/2Pの画面: どんな形の画面でも範囲が全部映るように、カメラを自動でズームする")]
    public bool autoFitPlayerScreens = true;
    [HideInInspector] public bool fillPlayerScreen = true;
    [Tooltip("棚の画面: 仮の棚(背板・柱・段の板)を出して、商品を板の上に乗せる。OFFなら今までどおり商品だけが浮いて並ぶ。棚の画像・3Dモデルは「棚の見た目」に入れる")]
    public bool showShelf = true;
    [Tooltip("棚の画面: 棚は横に長くつながっていて、Shelf Slide Interval 秒ごとに左から次の棚がスライドしてくる。OFF なら棚は1つだけで動かない")]
    public bool shelfSlide = true;
    [Tooltip("棚: ランダム補充。商品の一覧を山札にして、棚にはランダムに引いて置く。取った商品は山札に戻り、空いたマスには山札からランダムに補充。OFF なら、取った場所に同じ商品を補充")]
    public bool randomRefill = true;
    [Tooltip("棚の画面: 棚の商品の上に値札(名前と金額)を出す。献立の食材は黄色")]
    public bool showPriceTags = true;
    [Tooltip("棚の画面: 横のとき、配置が全部映るようにカメラを自動でズームする")]
    public bool autoFitShopScreen = true;
    [Tooltip("棚の画面: 縦のとき、上の空き場所に「TIMER」の目印を出す")]
    public bool showTimerPlaceholder = true;
    [Tooltip("棚の画面: 残りのプレイ時間(制限時間)を出す。縦のときは上の空き場所、横のときは画面の上")]
    public bool showGameTimer = true;
    [Tooltip("タイムアップ: 操作を止めて、棚の画面を左右2つに分けて勝ち負けを出す。Aボタンでもう一度。OFFなら「タイムアップ！」と出るだけで続けられる")]
    public bool showResultOnTimeUp = true;
    [Tooltip("タイムアップ: 勝ち負けの前に、棚の画面で合計金額の棒グラフ(1P 赤・2P 青)が下から伸びる結果発表を出す")]
    public bool showResultBars = true;
    [Tooltip("タイムアップ: 結果発表の前に「袋持ち上げターン」。1P/2P のレジ袋を持ち上げて数秒耐える。袋からこぼれて落ちた商品は消えて、その金額を合計から引く(詰め放題の「持ち上げてこぼれたら無し」)")]
    public bool bagLiftOnTimeUp = true;
    [Tooltip("エントリー画面: 再生したらまずエントリー画面を出し、1P/2P が A で参加したらカウントダウンして開始(制限時間は開始から数える)。OFF ならすぐ始まる")]
    public bool showEntryScreen = true;
    [Tooltip("コントローラー: F2(またはビューボタン長押し)で登録した 1P用・2P用のコントローラーを、いつも同じ側にする(登録はパソコンに保存される)。OFF なら、最初に A を押した方が 1P")]
    public bool fixedPadAssignment = true;
    [Tooltip("キーボードも予備として使える(1P: WASD+Space+Q / 2P: 矢印+Enter+右Shift)")]
    public bool useKeyboard = true;
    [Tooltip("画面の左上に、今起きたこと・コントローラーの状態を出す(確認用)")]
    public bool showOverlayLabel = true;

    [Header("画面の割り当て(0始まり。DisplayTestSwitcher の Screens の番号)")]
    [Tooltip("1Pの画面を、Screens の何番のカメラに映すか(0, 1, 2)。再生中に入れ替えてもすぐ反映。3つは別々の番号にする")]
    public int screenP1 = 0;     // 初期: ディスプレイ1 = 1P
    [Tooltip("棚の画面を、Screens の何番のカメラに映すか。棚をメインディスプレイに出すなら、そのカメラの番号にする")]
    public int screenShop = 1;   // 初期: ディスプレイ2 = 商品棚
    [Tooltip("2Pの画面を、Screens の何番のカメラに映すか")]
    public int screenP2 = 2;     // 初期: ディスプレイ3 = 2P
    [Tooltip("背景の色は、画面の番号ではなく役割について回る(入れ替えても 1P は赤のまま)")]
    public Color p1BackColor = new Color(0.45f, 0.12f, 0.12f);
    public Color shopBackColor = new Color(0.12f, 0.40f, 0.15f);
    public Color p2BackColor = new Color(0.12f, 0.20f, 0.50f);

    [Header("表示用レイヤー番号(名前の登録は不要)")]
    public int layerP1 = 8;
    public int layerShop = 9;
    public int layerP2 = 10;

    [Header("カーソル")]
    public float cursorSpeed = 8f;
    public Color p1Color = new Color(1f, 0.3f, 0.3f);
    public Color p2Color = new Color(0.3f, 0.5f, 1f);

    [Header("1P/2Pの画面(レジ袋の画面)。袋・机などの大きさは「モニターの置き方」の配置で変える")]
    [Tooltip("縦の画面の横:縦の比率(9:16 なら 0.5625)。横型(縦と同じ見た目)のときは、この形を横のモニターの真ん中に出す")]
    public float playerViewAspect = 9f / 16f;
    // 下の8つは「モニターの置き方」で選んだ配置(portraitLayout / landscapeLayout)から毎フレーム入る
    [HideInInspector] public float playerViewWidth = 10f;
    [HideInInspector] [Range(0f, 0.5f)] public float floorFromBottom = 0.04f;
    [HideInInspector] [Range(0.2f, 1f)] public float deskWidthRatio = 1f;
    [HideInInspector] public float deskThickness = 0.5f;
    [HideInInspector] [Range(0f, 1.2f)] public float dropHeightRatio = 0.85f;
    [HideInInspector] [Range(0.1f, 1f)] public float bagWidthRatio = 0.8f;
    [HideInInspector] [Range(0.05f, 1f)] public float bagHeightRatio = 0.65f;
    [HideInInspector] [Range(-0.5f, 0.5f)] public float bagXRatio = 0f;
    [Tooltip("机(床)と見えない壁を、画面の外まで何倍に広げるか(2で画面に見えている机の2倍。左右に半分ずつはみ出す)。商品は画面の外へ飛び出せる。カーソルと袋は画面の中のまま")]
    [Range(1f, 4f)] public float deskExtend = 2f;
    [Tooltip("縦型モニターのとき、1Pの画面(ディスプレイ1)を90度回して描く。Windows は横のまま、モニターだけ縦に置いたとき用(逆さに見えたらもう一方)。横型のときは回さない")]
    public ScreenRotation p1ScreenRotation = ScreenRotation.None;
    [Tooltip("縦型モニターのとき、2Pの画面(ディスプレイ3)を90度回して描く。Windows は横のまま、モニターだけ縦に置いたとき用(逆さに見えたらもう一方)。横型のときは回さない")]
    public ScreenRotation p2ScreenRotation = ScreenRotation.None;

    // 下の6つは、上の割合から自動で計算する(Inspector には出さない)
    [HideInInspector] public float dropHeight = 3.5f;

    [Header("カーソル画像(未設定なら矢印を自動生成)")]
    public Sprite p1CursorSprite;
    public Sprite p2CursorSprite;
    [HideInInspector] public float cursorScale = 1.2f;

    [Header("制限時間")]
    [Tooltip("1ゲームのプレイ時間(秒)。再生を始めたときから数える。120で2分")]
    public float gameTime = 120f;
    [Tooltip("エントリー画面: 2人そろってから始まるまでのカウントダウンの秒数")]
    public float entryCountdown = 3f;
    [Tooltip("エントリー画面: 棚の画面に出すゲーム名")]
    public string entryTitle = "詰め放題バトル";
    [Tooltip("残りがこの秒数以下になったら、時間の文字を赤くする")]
    public float timerWarningTime = 10f;
    [Tooltip("勝ち負けを出してから、Aボタンでもう一度遊べるようになるまでの秒数(連打でうっかり始まらないように)")]
    public float resultRestartDelay = 1.5f;
    [Tooltip("結果発表: 伸び始める位置から画面の上の端までが表す金額(カメラが引く前)")]
    public int resultBarScreenAmount = 10000;
    [Tooltip("結果発表: 棒の先がこの高さ(伸び始める位置〜画面の上の端を1として)まで来たら、カメラが棒の先を追いかけて上へ進む。1なら上の端(レジ袋と金額は画面の外になる)。0.7くらいならレジ袋と金額も見える")]
    [Range(0.1f, 1f)] public float resultBarFollowHeight = 0.7f;
    [Tooltip("結果発表: カメラが引いたとき、棒の太さ(と2本の間隔)はここまでしか細くならない(引く前を1として。0.5なら半分まで)")]
    [Range(0.05f, 1f)] public float resultBarMinWidth = 0.6f;
    [Tooltip("結果発表: カメラが引いたとき、金額・レジ袋・名前・目盛りの文字はここまでしか小さくならない(引く前を1として)")]
    [Range(0.05f, 1f)] public float resultBarMinTextSize = 0.7f;
    [Tooltip("結果発表: 速さの基準。棒が下の端から上の端まで(Result Bar Screen Amount ぶん)伸びる秒数。実際の速さは、これに下の Start/End Speed を掛ける")]
    public float resultBarGrowTime = 2.5f;
    [Tooltip("結果発表: 伸び始めの速さ(Grow Time の速さを1として。0.2なら5分の1のゆっくり)")]
    public float resultBarStartSpeed = 0.2f;
    [Tooltip("結果発表: いちばん速くなったときの速さ(Grow Time の速さを1として)")]
    public float resultBarEndSpeed = 2.5f;
    [Tooltip("結果発表: 伸び始めの速さから、いちばん速い速さになるまでの秒数(最初はゆっくり、だんだん速くなる)")]
    public float resultBarAccelTime = 3f;
    [Tooltip("結果発表: 伸び終わってから、全体が見えるように縮める(カメラが引く)動きの秒数。大きいほどゆっくり")]
    public float resultBarOverviewTime = 2.5f;
    [Tooltip("結果発表: 全体を見せてから、勝ち負けの画面になるまでの秒数")]
    public float resultBarHoldTime = 1.5f;
    [Tooltip("結果発表: 背景の色(上が上の色、下が下の色で、なめらかにつなぐ)")]
    public Color resultBarBackTop = new Color(0.86f, 0.9f, 0.96f);
    public Color resultBarBackBottom = new Color(0.97f, 0.93f, 0.84f);
    [Tooltip("結果発表: 文字(金額・名前・見出し・目盛り)の大きさ。1で前の大きさ")]
    public float resultBarTextScale = 1.5f;

    [HideInInspector] public float textScale = 2f;   // 「モニターの置き方」の配置から入る

    [Header("タイムアップ: 袋を持ち上げる(機能のON/OFF の Bag Lift On Time Up)")]
    [Tooltip("タイムアップしてから持ち上げ始めるまでの秒数(落ちている途中の商品が袋に入るのを待つ)")]
    public float bagLiftDelay = 0.8f;
    [Tooltip("持ち上げる高さ(1P/2Pの画面の高さを1として。0.2なら2割)。袋の上の合計金額が画面からはみ出さない高さまでで止まる")]
    [Range(0f, 0.6f)] public float bagLiftRatio = 0.2f;
    [Tooltip("持ち上げるのにかかる秒数(小さいほど速く持ち上がり、商品がこぼれやすい)")]
    public float bagLiftTime = 1.5f;
    [Tooltip("「袋持ち上げターン！」を出してから、持ち上げ始めるまでの秒数")]
    public float bagLiftAnnounceTime = 1.5f;
    [Tooltip("持ち上げたまま耐える秒数(「耐えろ！ あと3」と数える)。耐えたら合計金額の結果発表へ")]
    public float bagLiftHoldTime = 3f;

    [Header("コントローラー")]
    [Tooltip("スティックの遊び(これ未満は無視)")]
    public float stickDeadzone = 0.2f;

    [Header("落下(物理)")]
    [Tooltip("重力の強さ(大きいほど速く落ちる)")]
    public float gravityScale = 2f;
    [HideInInspector] public float floorY = -4.5f;   // 自動で計算(floorFromBottom)
    [HideInInspector] public float wallX = 8.9f;     // 自動で計算(画面に見えている机の端。カーソル・袋はこの中)
    float DeskOuterX => wallX * Mathf.Max(1f, deskExtend);   // 本当の机の端と見えない壁(画面の外まで続く)
    [Tooltip("ONなら、落下中に商品が回転しない")]
    public bool freezeRotation = false;

    [Header("商品の一覧(棚の左上から順に並ぶ。画像・3Dモデルは各商品の Sprite / Model に入れる)")]
    [Tooltip("1行が商品1つ(名前・ランク・金額・献立の食材か・仮の図形・画像・3Dモデル)。棚のマス(段 x 列)より多いぶんは棚に出ない。並べたい順に入れ替える。空にすると前の設定(Product Shapes など)を使う")]
    public ProductInfo[] productList = ProductInfo.Defaults();

    // 前の設定(商品の一覧が空のときだけ使う。Inspector には出さない)
    [HideInInspector] public ProductShape[] productShapes = {
        ProductShape.Square, ProductShape.Triangle, ProductShape.Rectangle,
        ProductShape.LShape, ProductShape.Hexagon, ProductShape.TShape
    };

    [HideInInspector] public Sprite[] productSprites = new Sprite[6];
    [HideInInspector] public float productSize = 1.4f;
    [HideInInspector] public float productImageSize = 2f;   // 前の設定(今は productSize を使う)

    [Header("画像の商品の当たり判定(画像の形を、点の数を減らして簡単にする。細かすぎると物理の計算が重くなる)")]
    [Tooltip("形をどれだけ大まかにするか(画像の大きさに対する割合)。小さいほど画像の形に近いが重くなる")]
    [Range(0.005f, 0.1f)] public float imageColliderDetail = 0.02f;
    [Tooltip("当たり判定の点の数の上限。多いほど画像の形に近いが重くなる")]
    [Range(4, 64)] public int imageColliderMaxPoints = 24;

    [Header("割れる商品(どれが割れるか・どのくらいの重さで割れるかは、商品の一覧の Fragile / Break Load)")]
    [Tooltip("重さが Break Load を超えた状態がこの秒数続いたら割れる(落ちてきた瞬間の衝撃では割れないように)")]
    public float breakHoldTime = 0.3f;

    [Header("袋が破れる(最後に持ち上げたとき。機能のON/OFF の Bag Tear Enabled)")]
    [Tooltip("袋の中の商品の重さの合計がこれを超えていたら、持ち上げたときに破れる(重さ 1 の商品が何個ぶんか)。重さは画面には出さない")]
    public float bagMaxWeight = 15f;
    [Tooltip("上まで持ち上げてから、破れるまでの秒数(耐えている途中で破れる。Bag Lift Hold Time より長くすると、耐える時間の最後に破れる)")]
    public float bagTearDelay = 2f;
    [Tooltip("破れたとき、底から落ちる中身が左右に散らばる強さ(0ならまっすぐ下へ落ちる)")]
    public float bagTearScatter = 1.5f;

    [Header("3Dモデルの商品の大きさ")]
    [Tooltip("OFF: どのモデルも、縦横の長いほうがふつうの商品の大きさになる(モデルどうしの大きさの違いは無くなる)。ON: もらったモデルの大きさのまま出す(x Model Unit Scale)。どちらも、さらに一覧の Model Size をかける")]
    public bool modelOriginalSize = false;
    [Tooltip("Model Original Size が ON のとき、モデルの大きさにかける倍率(モデルの1メートル = ゲームの何マスか。ゲームの商品はだいたい 1.4 マス)")]
    public float modelUnitScale = 1f;

    [Header("物理の重さ")]
    [Tooltip("ON なら、商品ごとの重さを物理の重さ(mass)にも使う(重い商品ほど下の商品を強く押す)。OFF なら全部同じ重さで動く(袋のふちをすり抜けにくい)。袋が破れる重さはどちらでも同じ")]
    public bool weightAffectsPhysics = false;

    [Header("袋がきしむ擬音(破れる重さに近づいたら「ミシ…」)")]
    [Tooltip("擬音が出はじめる重さ(破れる重さ Bag Max Weight を1として。0.7なら7割)")]
    [Range(0f, 0.99f)] public float bagCreakStart = 0.7f;
    [Tooltip("出す擬音(この中からランダム)")]
    public string[] bagCreakWords = { "ミシ…", "ミシミシ…", "ギシ…", "ギチ…" };
    [Tooltip("擬音の文字の大きさ(1で普通)")]
    public float bagCreakSize = 1f;

    [Header("献立ミッション(食材5品が全部袋に入ったら、それ以降の商品が倍)")]
    [Tooltip("献立と食材(食材は商品の一覧の名前と同じにする)。1試合に、この中から1つだけ出る")]
    public MenuRecipe[] menuList = MenuRecipe.Defaults();
    [Tooltip("この試合の献立を決める(Menu List の番号。0始まり)。-1 ならランダム")]
    public int menuFixedIndex = -1;
    [Tooltip("袋の画面の献立の表示の場所(画面の真ん中が 0、右の端が 0.5、上の端が 0.5)。文字の左上の位置")]
    public Vector2 menuLabelPosition = new Vector2(-0.45f, 0.3f);
    [Tooltip("袋の画面の献立の文字の大きさ(1で普通)")]
    public float menuLabelSize = 1f;
    [Tooltip("まだ棚に出ていない献立の食材を、山札から引くときに優先する確率(0.25なら4回に1回)。残り時間が少なくなったら必ず出す")]
    [Range(0f, 1f)] public float menuIngredientBoost = 0.25f;
    [Tooltip("完成したあとに袋に入れた商品の金額の倍率")]
    public float menuBonusRate = 1.2f;
    [Tooltip("「○○完成！」を出しておく秒数")]
    public float menuCompleteTime = 2.5f;

    [Header("棚の補充")]
    [Tooltip("取られてから補充されるまでの秒数")]
    public float refillDelay = 1.5f;

    [Header("レジ袋(1P/2Pの画面)")]
    [HideInInspector] public float bagX = 0f;        // 自動で計算(bagXRatio)
    [HideInInspector] public float bagWidth = 5f;    // 自動で計算(bagWidthRatio)
    [HideInInspector] public float bagHeight = 4f;   // 自動で計算(bagHeightRatio)
    [Tooltip("袋のふちの太さ")]
    public float bagWallThickness = 0.3f;
    [Tooltip("袋の左右のふちの当たり判定を、見た目より外側へ厚くする長さ(商品がふちをすり抜けないように。見た目は変わらない)")]
    public float bagWallOuterThickness = 0.6f;
    public Color bagColor = new Color(1f, 1f, 1f, 0.85f);
    [Tooltip("袋の画像(未設定なら白いU字)。画像は袋の幅・高さに引き伸ばして表示し、商品がぶつかるふちは見えない壁になる")]
    public Sprite bagSprite;
    [HideInInspector] public int[] productPrices = { 100, 150, 200, 250, 300, 500 };
    [Tooltip("袋に入ったときに出る金額の表示時間(秒)")]
    public float pricePopupTime = 1f;
    [Tooltip("袋の外で止まってから消えるまでの秒数(袋のふちや他の商品の上に乗ったとき)")]
    public float removeAfterStopTime = 0.5f;

    [Header("掴み合い(2人が同じ商品を掴んだとき)")]
    [Tooltip("1人が掴んでからこの秒数以内に、もう1人が同じ場所でAを押すと掴み合いになる。0なら掴み合いなし")]
    public float grabWindow = 0.5f;
    [Tooltip("連打: Aボタンを連打して、先に決まった回数押した方が勝ち / タイミング: 往復するゲージを最大に近いところで止めた方が勝ち(前の方式)")]
    public ClashMode clashMode = ClashMode.Mash;
    [Tooltip("連打のとき、先にこの回数Aを押した方が勝ち")]
    public int clashMashCount = 10;
    [Tooltip("ゲージが 0→最大→0 と往復する速さ(1秒あたりの往復回数。タイミングのとき)")]
    public float clashGaugeSpeed = 1.2f;
    [Tooltip("ゲージがこれ以上で止めたら「パーフェクト!」(1が最大。タイミングのとき)")]
    [Range(0f, 1f)] public float clashPerfect = 0.95f;
    [Tooltip("勝ち負けを表示しておく秒数")]
    public float clashResultTime = 1.2f;
    [Tooltip("掴み合いが決着したとき、1P/2Pの画面に「勝ち」「負け」を出しておく秒数")]
    public float clashWinLoseTime = 2f;
    [Tooltip("真ん中にしないときのゲージの高さ(横の棚の画面。中心が0、下の端が-5。縦のときは画面の下)")]
    public float clashGaugeY = -3.6f;

    [Header("キック(LBで蹴る)")]
    [Tooltip("キックのゲージが 0→最大→0 と往復する速さ(1秒あたりの往復回数)")]
    public float kickGaugeSpeed = 1.2f;
    [Tooltip("ゲージがこれ以上で止めたら「パーフェクト!」(1が最大)")]
    [Range(0f, 1f)] public float kickPerfect = 0.95f;
    [Tooltip("1回蹴ってから、次に蹴れるまでの秒数")]
    public float kickCooldown = 3f;
    [Tooltip("足が棚の画面を飛んでいく秒数")]
    public float kickFlightTime = 0.7f;
    [Tooltip("足の画像(未設定なら自動生成の靴)。つま先が右を向いた画像を想定")]
    public Sprite kickFootSprite;
    [Tooltip("足の大きさ(縦横の長いほうがこの長さになる)")]
    public float kickFootSize = 2.5f;
    [Tooltip("足の画像の向きの調整(度)。つま先が飛ぶ方向を向かないときに変える")]
    public float kickFootRotation = 0f;
    [Tooltip("威力が最大のときの、相手のレジ袋の左右の揺れ幅")]
    public float kickShakeMax = 3f;
    [Tooltip("威力が最大のときの、相手のレジ袋が上に跳ねる高さ(中の商品が浮く)")]
    public float kickShakeUp = 0.3f;
    [Tooltip("1秒に揺れる回数")]
    public float kickShakeFrequency = 4f;
    [Tooltip("同じ机なので、蹴った人のレジ袋も揺れる。相手の揺れに対する割合(0.3なら3割。0なら揺れない)")]
    [Range(0f, 1f)] public float kickSelfShake = 0.3f;
    [Tooltip("足が届いたときに、1P/2Pの画面(カメラ)がずれる大きさ(威力が最大のとき)。0なら画面は揺れない。棚の画面は揺れない")]
    public float kickScreenShake = 0.6f;
    [Tooltip("画面が揺れる秒数")]
    public float kickScreenShakeTime = 0.5f;
    [Tooltip("相手のレジ袋が揺れる秒数")]
    public float kickShakeTime = 1f;

    [Header("棚の画面(画面2)。縦/横は「モニターの置き方」で決まる")]
    [HideInInspector] public ShopOrientation shopOrientation = ShopOrientation.Landscape;
    [Tooltip("縦のときの画面の横:縦の比率(9:16なら0.5625)。モニターが決まったら合わせる")]
    public float portraitAspect = 9f / 16f;
    [Tooltip("縦のときに映る横幅。小さいほど棚が大きく映る")]
    public float portraitViewWidth = 8f;
    [Tooltip("縦のとき、上を空ける割合(タイマー用。0.2なら上の2割)")]
    [Range(0f, 0.5f)] public float portraitTopSpace = 0.2f;

    [Header("棚の段と列(商品の数 = 段 x 列。再生中に変えてもすぐ反映)")]
    [Tooltip("縦のときの棚の列の数(横に並ぶ数)")]
    public int portraitColumns = 2;
    [Tooltip("縦のときの棚の段の数(縦に並ぶ数)")]
    public int portraitRows = 3;
    [Tooltip("横のときの棚の列の数")]
    public int landscapeColumns = 3;
    [Tooltip("横のときの棚の段の数")]
    public int landscapeRows = 2;

    [Header("棚のスライド(数秒ごとに、左から次の棚がスライドしてくる)")]
    [Tooltip("次の棚がスライドしてくるまでの秒数(スライドが終わってから数える)")]
    public float shelfSlideInterval = 8f;
    [Tooltip("棚がスライドする動きの秒数")]
    public float shelfSlideTime = 1f;

    [Header("棚の見た目(画像か3Dモデルを入れると、仮の棚の代わりに出る)")]
    [Tooltip("仮の棚の背板の色")]
    public Color shelfBackColor = new Color(0.35f, 0.24f, 0.15f);
    [Tooltip("仮の棚の柱・段の板の色")]
    public Color shelfBoardColor = new Color(0.62f, 0.45f, 0.28f);
    [Tooltip("段の板の厚さ(商品はこの上に乗る)")]
    public float shelfBoardThickness = 0.25f;
    [Tooltip("棚の画像。入れると仮の棚の代わりに、棚の範囲いっぱいに引き伸ばして出す。商品の位置は段と列で決まるので、絵の板の位置と段の数を合わせる")]
    public Sprite shelfSprite;
    [Tooltip("棚の3Dモデル(プレハブ)。入れると仮の棚・画像の代わりに、棚の範囲に収まる大きさで商品の奥に出す。暗く・黒く見えたらマテリアルを Unlit にする")]
    public GameObject shelfModel;
    [Tooltip("3Dモデルの位置のずらし")]
    public Vector3 shelfModelOffset = Vector3.zero;
    [Tooltip("3Dモデルの向き(度)。正面がこちらを向かないときに変える(例: Y を 180)")]
    public Vector3 shelfModelRotation = Vector3.zero;
    [Tooltip("3Dモデルの大きさ(1なら棚の範囲にちょうど収まる)")]
    public float shelfModelScale = 1f;

    [Header("動作")]
    public bool arrangeCameras = true;

    class Player
    {
        public string name;
        public int screen;
        public int layer;
        public Transform cursorShop;     // 棚画面のカーソル
        public Transform cursorOwn;      // 自分の画面のカーソル
        public Vector2 shopPos;
        public Vector2 ownPos;
        public Color color;
        public bool inShop = true;       // true: 棚を操作中 / false: 自分の画面を操作中
        public GameObject held;          // 掴んでいる商品(自分の画面で持ち運び中)
        public readonly List<GameObject> dropped = new List<GameObject>();   // 自分の画面に落とした商品(まだ袋に入っていない)
        public readonly List<GameObject> inBag = new List<GameObject>();     // 袋に入った商品
        public Bag bag;
        public int total;                // 袋に入った商品の合計金額
        public GameObject lastTaken;     // 最後に棚から取った商品(掴み合いの判定用)
        public float lastTakenTime;
        public Bounds lastTakenBounds;   // 取ったときの棚での範囲
        public Vector3 lastTakenShelfPos;
        public KickGauge kick;           // キックのゲージ中なら null 以外
        public float kickCooldown;       // 0より大きい間は蹴れない
        public TextMesh kickLabel;       // 自分の画面の「LB: キック」表示
        public Coroutine shakeRoutine;   // 自分のレジ袋が揺れている処理
        public Coroutine screenShakeRoutine;   // 自分の画面(カメラ)が揺れている処理
        public string completedMenu;     // 完成した献立の名前(献立ミッション。まだなら null)
        public bool hasCameraBase;
        public Vector3 cameraBase;       // 揺れていないときのカメラの位置
    }

    // 棚の1か所ぶん(どこに・どんな商品を置くか)
    class ShelfSlot
    {
        public Vector2 pos;
        public ProductShape shape;
        public Color color;
        public int count;   // これまでに置いた個数(名前の番号に使う)
        public int product = -1;   // 置く商品(商品の一覧の番号。ランダム補充のとき。-1 = 空き)
        public GameObject go;      // 今置いてある商品(無ければ null)
        public TextMesh tag;       // このマスの値札
    }

    Player p1, p2;
    readonly List<ShelfSlot> slots = new List<ShelfSlot>();
    readonly Dictionary<GameObject, ShelfSlot> slotOf = new Dictionary<GameObject, ShelfSlot>();
    readonly Dictionary<GameObject, int> priceOf = new Dictionary<GameObject, int>();   // 商品ごとの金額
    static Font labelFont;
    static Sprite gaugeSprite;
    readonly Dictionary<Collider2D, GameObject> products = new Dictionary<Collider2D, GameObject>();
    readonly Dictionary<GameObject, Color> originalColors = new Dictionary<GameObject, Color>();
    static Sprite squareSprite, circleSprite, arrowSprite;
    string lastEvent = "";
    readonly List<Transform> floors = new List<Transform>();   // 1P/2P画面の床
    float appliedFloorY;   // 床に今反映している高さ
    float appliedWallX;    // 壁に今反映している位置
    float appliedDeskThickness;
    int dropOrder = 0;   // 落とした順に手前へ重ねる
    Gamepad pad1, pad2;  // 参加済みのコントローラー(Aボタンで割り当て)

    // ---------- 初期化 ----------

    void Start()
    {
        ApplyTextFont();   // 文字のフォントと解像度(TwoPlayerShop2D.Bag.cs)。文字を作る前に決める
        if (switcher == null) switcher = FindAnyObjectByType<DisplayTestSwitcher>();
        if (switcher == null) { Debug.LogError("[Shop2D] DisplayTestSwitcher が見つかりません"); return; }
        ApplyMonitorMode();   // 縦型/横型の配置を、袋・机・商品・文字などの設定に入れる
        RestorePads();   // もう一度遊ぶとき、前のゲームのコントローラーの割り当てを戻す
        ChooseMenu();    // この試合の献立(TwoPlayerShop2D.Menu.cs)

        SetupPhysicsLayers();
        CreateProducts();
        if (arrangeCameras) ArrangeCameras();

        p1 = CreatePlayer("1P", screenP1, layerP1, p1Color, p1CursorSprite, new Vector2(-4f, -3f));
        p2 = CreatePlayer("2P", screenP2, layerP2, p2Color, p2CursorSprite, new Vector2(4f, -3f));

        ApplyScreenAssignment();   // 1P/棚/2P をどのカメラに映すか(背景の色・映すもの)
        ApplyShopLayout();
        ApplyPlayerLayout();   // 床・袋・落とす高さを、1P/2Pの画面の範囲から計算する

        if (createFloor)
        {
            CreateFloor(layerP1);
            CreateFloor(layerP2);
        }

        if (createBag)
        {
            p1.bag = CreateBag(p1);
            p2.bag = CreateBag(p2);
            LayoutBags();
        }

        // 最初は両プレイヤーとも棚を操作中
        foreach (var p in new[] { p1, p2 })
        {
            p.cursorShop.gameObject.SetActive(true);
            p.cursorOwn.gameObject.SetActive(false);
        }
    }

    // 3つの画面は同じワールド座標を使っているので、
    // 画面(レイヤー)が違う物同士は物理的にぶつからないようにする
    void SetupPhysicsLayers()
    {
        Physics2D.IgnoreLayerCollision(layerP1, layerP2, true);
        Physics2D.IgnoreLayerCollision(layerP1, layerShop, true);
        Physics2D.IgnoreLayerCollision(layerP2, layerShop, true);
        // 同じ画面の中(1P同士、2P同士)はぶつかる
        Physics2D.IgnoreLayerCollision(layerP1, layerP1, false);
        Physics2D.IgnoreLayerCollision(layerP2, layerP2, false);
        // 積み重なった商品が、重さで袋のふちにめり込んだり、すり抜けたりしにくくなるように、物理の計算を丁寧にする
        Physics2D.velocityIterations = Mathf.Max(Physics2D.velocityIterations, 12);
        Physics2D.positionIterations = Mathf.Max(Physics2D.positionIterations, 8);
    }

    void CreateProducts()
    {
        int count = TotalSlotCount;   // 段 x 列 x 棚の数(TwoPlayerShop2D.Shelf.cs)
        if (UseDeck) InitDeck();   // 山札を作り直す(TwoPlayerShop2D.Deck.cs)
        for (int i = 0; i < count; i++)
        {
            // ランダム補充: 山札からランダムに引く / そうでなければ「商品の一覧」の i 番目(一覧が短ければ繰り返す)
            var slot = new ShelfSlot { pos = ShelfSlotPosition(i) };
            slots.Add(slot);
            if (UseDeck) slot.product = DrawCard();
            if (!UseDeck || slot.product >= 0) PlaceProduct(slot);
        }
    }

    // 棚の場所に商品を1個置く
    void PlaceProduct(ShelfSlot slot)
    {
        slot.count++;
        var info = SlotInfo(slot);
        slot.shape = info.shape;
        slot.color = RankColor(info.rank);
        string objName = slot.count == 1 ? info.name : $"{info.name}_{slot.count}";
        // 3Dモデル → 画像 → 仮の図形 の順に、入っているものを使う
        var go = info.model != null ? MakeModelProduct(objName, slot.pos, info, layerShop)
                                    : MakeSprite(objName, slot.pos, slot.shape, slot.color, layerShop, info.sprite);
        // 商品ごとの大きさ。当たり判定も一緒に変わる(0 以下なら 1 のまま)
        //  - 3Dモデルの商品は Size を使わず、Model Size を使う(Size は画像・図形に合わせた値なので)
        float scale = info.model != null ? info.modelSize : info.size;
        if (scale > 0f) go.transform.localScale *= scale;
        PutOnShelf(go, slot);   // 棚を出すときは板の上に乗せる
        RegisterProduct(go);
        slotOf[go] = slot;
        slot.go = go;
        if (UseDeck) MarkShown(slot.product);   // この試合で棚に出した(献立の食材を必ず出すため。TwoPlayerShop2D.Deck.cs)
        SetSlotTag(slot);   // 値札をこの商品に
        priceOf[go] = info.price;
        infoOf[go] = info;   // 割れるか・重さ(TwoPlayerShop2D.Fragile.cs)
    }

    System.Collections.IEnumerator RefillLater(ShelfSlot slot, GameObject taken)
    {
        yield return new WaitForSeconds(refillDelay);
        // 掴み合い中は、取り合っている商品が棚に戻っているので補充を待つ
        while (clash != null && clash.product == taken) yield return null;
        if (!slots.Contains(slot)) yield break;   // 待っている間に段・列が変わって、この場所が無くなった
        if (slot.go != null) yield break;         // 待っている間に、もう置かれた(棚が回ったとき)
        if (UseDeck)
        {
            slot.product = DrawCard();             // 山札からランダムに1枚(TwoPlayerShop2D.Deck.cs)
            if (slot.product < 0) yield break;     // 山札が空(次に棚が回ってきたときに引く)
        }
        PlaceProduct(slot);
    }

    GameObject MakeSprite(string objName, Vector2 pos, ProductShape shape, Color color, int layer, Sprite image = null)
    {
        var go = new GameObject(objName);
        go.transform.position = pos;
        go.transform.localScale = new Vector3(productSize, productSize, 1f);   // 図形の絵は 1x1 に収まる

        var sr = go.AddComponent<SpriteRenderer>();
        if (image != null)
        {
            // 用意した画像: 色はそのまま、当たり判定は画像の形を簡単にしたもの(TwoPlayerShop2D.Products.cs)
            // (当たり判定は画像を入れる前に付ける。先に画像を入れると、Unity が細かすぎる形を自動で作って重くなる)
            go.AddComponent<PolygonCollider2D>().points = SimpleOutline(image);
            sr.sprite = image;
            sr.color = Color.white;
            Vector2 size = image.bounds.size;
            float scale = productSize / Mathf.Max(size.x, size.y, 0.01f);
            go.transform.localScale = new Vector3(scale, scale, 1f);
        }
        else
        {
            sr.sprite = GetShapeSprite(shape);
            sr.color = color;
            if (shape == ProductShape.Circle) go.AddComponent<CircleCollider2D>();
            else if (shape == ProductShape.Square) go.AddComponent<BoxCollider2D>();
            else go.AddComponent<PolygonCollider2D>().points = ShapePoints(shape);   // 見た目と同じ形の当たり判定
        }

        go.layer = layer;
        return go;
    }

    void RegisterProduct(GameObject go)
    {
        originalColors[go] = go.GetComponent<SpriteRenderer>().color;
        foreach (var col in go.GetComponentsInChildren<Collider2D>())
            products[col] = go;
    }

    Player CreatePlayer(string pname, int screen, int ownLayer, Color color, Sprite cursorSprite, Vector2 startPos)
    {
        var p = new Player { name = pname, screen = screen, layer = ownLayer, color = color, shopPos = startPos, ownPos = Vector2.zero };

        // 棚画面に出すカーソル(レイヤー: 棚)
        p.cursorShop = MakeCursor($"Cursor_{pname}_Shop", color, layerShop, cursorSprite).transform;
        p.cursorShop.position = startPos;

        // 自分の画面に出すカーソル(商品を落とす位置を選ぶ用)
        p.cursorOwn = MakeCursor($"Cursor_{pname}_Own", color, ownLayer, cursorSprite).transform;
        p.cursorOwn.position = Vector2.zero;
        return p;
    }

    GameObject MakeCursor(string objName, Color color, int layer, Sprite customSprite)
    {
        var go = new GameObject(objName);
        var sr = go.AddComponent<SpriteRenderer>();

        if (customSprite != null)
        {
            sr.sprite = customSprite;
            sr.color = Color.white;      // 画像そのままの色で表示
        }
        else
        {
            sr.sprite = GetArrowSprite();
            sr.color = color;            // 自動生成の矢印は1P赤/2P青で着色
        }

        // 縦横の長いほうを cursorScale の長さにそろえる(大きい画像でも巨大にならない)
        Vector2 size = sr.sprite.bounds.size;
        float scale = cursorScale / Mathf.Max(size.x, size.y, 0.01f);
        go.transform.localScale = new Vector3(scale, scale, 1f);

        sr.sortingOrder = 1000;   // 商品より常に手前
        go.layer = layer;
        // カーソルにコライダーは付けない(掴み判定の邪魔になるため)
        return go;
    }

    void ArrangeCameras()
    {
        for (int i = 0; i < switcher.screens.Length; i++)
        {
            var cam = switcher.screens[i].camera;
            if (cam == null) continue;

            cam.orthographic = true;
            cam.orthographicSize = 5f;
            cam.transform.position = new Vector3(0f, 0f, -10f);
            cam.transform.rotation = Quaternion.identity;
            cam.clearFlags = CameraClearFlags.SolidColor;
        }
    }

    // 指定レイヤー(=その画面だけ)に映る床と左右の壁を作る
    void CreateFloor(int layer)
    {
        var root = new GameObject($"Floor_Layer{layer}");
        root.layer = layer;

        // 床(見える)
        var floor = new GameObject("Floor");
        floor.transform.SetParent(root.transform);
        floor.layer = layer;
        floor.transform.position = new Vector3(0f, floorY - deskThickness / 2f, 0f);
        floor.transform.localScale = new Vector3(DeskOuterX * 2f, deskThickness, 1f);
        floors.Add(floor.transform);
        appliedFloorY = floorY;
        appliedWallX = DeskOuterX;
        var sr = floor.AddComponent<SpriteRenderer>();
        sr.sprite = GetSquareSprite();
        sr.color = new Color(0.2f, 0.2f, 0.2f, 0.9f);
        sr.sortingOrder = -10;
        floor.AddComponent<BoxCollider2D>();

        // 左右の壁(見えない)
        for (int i = 0; i < 2; i++)
        {
            float sign = i == 0 ? -1f : 1f;
            var wall = new GameObject(i == 0 ? "WallLeft" : "WallRight");
            wall.transform.SetParent(root.transform);
            wall.layer = layer;
            wall.transform.position = new Vector3(sign * (DeskOuterX + 0.5f), 0f, 0f);
            wall.transform.localScale = new Vector3(1f, 40f, 1f);
            wall.AddComponent<BoxCollider2D>().size = Vector2.one;
        }
    }

    // ---------- メインループ ----------

    void Update()
    {
        if (switcher == null || p1 == null) return;

        ApplyMonitorMode();
        ApplyPlayerLayout();
        if (floorY != appliedFloorY || DeskOuterX != appliedWallX || deskThickness != appliedDeskThickness) ApplyFloorY();

        ApplyBagTotalVisibility();   // 合計金額の表示の ON/OFF(エントリー画面でも同じ。TwoPlayerShop2D.Bag.cs)
        UpdateMenuLabels();          // この試合の献立と、そろった食材(TwoPlayerShop2D.Menu.cs)

        // エントリー画面: 2人が A で参加 → カウントダウン → 開始(TwoPlayerShop2D.Entry.cs)
        if (UpdateEntry()) return;

        // タイムアップ: 操作と点数を止めて、勝ち負けを出す(Aでもう一度)
        if (showResultOnTimeUp && IsTimeUp) { UpdateResult(); return; }
        if (createBag && p1.bag != null)
        {
            if (bagX != appliedBagX || bagWidth != appliedBagWidth || bagHeight != appliedBagHeight ||
                bagWallThickness != appliedBagWall || floorY != appliedBagFloorY ||
                bagWallOuterThickness != appliedBagOuter) LayoutBags();
            CheckBag(p1);
            CheckBag(p2);
        }

        // コントローラー登録中(F2)はゲームを止める(TwoPlayerShop2D.Pads.cs)
        if (UpdatePadRegistration()) return;

        // 参加(登録したコントローラーを 1P/2P に。登録が無ければ A ボタンを押した順に 1P→2P)
        TryAssignPads(out bool joined1, out bool joined2);

        bool press1 = !joined1 && Grab1PPressed();   // 参加したフレームのAボタンは「掴む」として扱わない
        bool press2 = !joined2 && Grab2PPressed();
        bool kick1 = Kick1PPressed(), kick2 = Kick2PPressed();

        if (clash != null)
        {
            // 掴み合い中: カーソルは止めて、Aボタンは連打(またはゲージを止める)に使う
            MoveCursor(p1, Vector2.zero);
            MoveCursor(p2, Vector2.zero);
            UpdateClash(press1, press2);
            return;
        }

        // キックのゲージ中はカーソルを止める
        MoveCursor(p1, p1.kick != null ? Vector2.zero : GetMove1P());
        MoveCursor(p2, p2.kick != null ? Vector2.zero : GetMove2P());

        if (kick1) ToggleKick(p1);
        if (kick2) ToggleKick(p2);

        // キックのゲージ中のAボタンは、威力を決めるのに使う
        if (press1) { if (p1.kick != null) LockKick(p1); else PressAction(p1); }
        if (press2) { if (p2.kick != null) LockKick(p2); else PressAction(p2); }

        // RB: 持っている商品を90度回す
        if (Rotate1PPressed()) RotateHeld(p1);
        if (Rotate2PPressed()) RotateHeld(p2);

        UpdateKick(p1);
        UpdateKick(p2);
    }

    // Inspector で床の高さを変えたら、床を動かす
    void ApplyFloorY()
    {
        appliedFloorY = floorY;
        appliedWallX = DeskOuterX;
        appliedDeskThickness = deskThickness;
        foreach (var f in floors)
        {
            f.position = new Vector3(f.position.x, floorY - deskThickness / 2f, f.position.z);
            f.localScale = new Vector3(DeskOuterX * 2f, Mathf.Max(0.05f, deskThickness), 1f);
            // 左右の壁も範囲の端へ
            var left = f.parent != null ? f.parent.Find("WallLeft") : null;
            var right = f.parent != null ? f.parent.Find("WallRight") : null;
            if (left != null) left.position = new Vector3(-(DeskOuterX + 0.5f), 0f, 0f);
            if (right != null) right.position = new Vector3(DeskOuterX + 0.5f, 0f, 0f);
        }

        // 床の上で止まっている商品を起こす(床を下げたとき宙に浮いたままにならないように)
        foreach (var go in originalColors.Keys)
        {
            var rb = go != null ? go.GetComponent<Rigidbody2D>() : null;
            if (rb != null && rb.bodyType == RigidbodyType2D.Dynamic) rb.WakeUp();
        }
    }

    void TryAssignPads(out bool joined1, out bool joined2)
    {
        joined1 = joined2 = false;

        // 抜かれたコントローラーは外す
        if (pad1 != null && !pad1.added) pad1 = null;
        if (pad2 != null && !pad2.added) pad2 = null;

        if (HasPadRegistration) AssignRegisteredPads();   // 登録したコントローラー(TwoPlayerShop2D.Pads.cs)
        bool fixed1 = HasPadRegistration && SavedPadId(0) != "", fixed2 = HasPadRegistration && SavedPadId(1) != "";

        foreach (var g in Gamepad.all)
        {
            if (g == pad1 || g == pad2 || IsRegisteredPad(g)) continue;   // 登録したものは、決まった方にだけ入る
            if (!g.buttonSouth.wasPressedThisFrame && !g.startButton.wasPressedThisFrame) continue;

            if (pad1 == null && !fixed1) { pad1 = keptPad1 = g; joined1 = true; Debug.Log($"[Shop2D] 1Pに割り当て: {g.displayName}"); }
            else if (pad2 == null && !fixed2) { pad2 = keptPad2 = g; joined2 = true; Debug.Log($"[Shop2D] 2Pに割り当て: {g.displayName}"); }
        }
    }

    void MoveCursor(Player p, Vector2 input)
    {
        Vector2 delta = input * cursorSpeed * Time.deltaTime;

        // 操作中の画面の位置だけを動かす
        if (p.inShop) p.shopPos = ClampToShopView(p.shopPos + delta);
        else if (horizontalOnlyOnOwnScreen)
            p.ownPos = new Vector2(Mathf.Clamp(p.ownPos.x + delta.x, -(wallX - 0.4f), wallX - 0.4f), dropHeight);   // 自分の画面では横移動だけ(左右は範囲の中)
        else
        {
            // 上下にも動ける(範囲の中、床より上)
            Vector2 view = PlayerViewSize();
            p.ownPos = new Vector2(Mathf.Clamp(p.ownPos.x + delta.x, -(wallX - 0.4f), wallX - 0.4f),
                                   Mathf.Clamp(p.ownPos.y + delta.y, floorY + 1f, view.y / 2f - 0.4f));
        }

        p.cursorShop.position = p.shopPos;
        p.cursorOwn.position = p.ownPos;

        // 操作中の画面のカーソルだけ表示する
        p.cursorShop.gameObject.SetActive(p.inShop);
        p.cursorOwn.gameObject.SetActive(!p.inShop);

        // 掴んでいる商品はカーソルについてくる
        if (p.held != null)
            p.held.transform.position = new Vector3(p.ownPos.x, p.ownPos.y, 0f);
    }

    static Vector2 ClampToView(Vector2 v)
    {
        return new Vector2(Mathf.Clamp(v.x, -8.5f, 8.5f), Mathf.Clamp(v.y, -4.5f, 4.5f));
    }

    // Aボタン: 棚なら「掴む」、自分の画面なら「離す」
    void PressAction(Player p)
    {
        if (p.inShop) TryPickUp(p);
        else Drop(p);
    }

    void TryPickUp(Player p)
    {
        // もう1人が grabWindow 秒以内に取った商品の場所なら、掴み合い
        var other = p == p1 ? p2 : p1;
        if (clashEnabled && grabWindow > 0f && other.lastTaken != null && other.held == other.lastTaken &&
            Time.time - other.lastTakenTime <= grabWindow &&
            other.lastTakenBounds.Contains(new Vector3(p.shopPos.x, p.shopPos.y, other.lastTakenBounds.center.z)))
        {
            var taken = other.lastTaken;
            // 取った人の画面から棚へ戻す
            other.held = null;
            other.inShop = true;
            other.lastTaken = null;
            SetLayer(taken, layerShop);
            taken.transform.position = other.lastTakenShelfPos;
            StartClash(taken);
            return;
        }

        Collider2D col = Physics2D.OverlapPoint(p.shopPos, 1 << layerShop);
        if (col == null || !products.TryGetValue(col, out var go)) return;
        // 棚の商品は、全部が画面に映っていないと取れない(TwoPlayerShop2D.Shelf.cs)
        if (slotOf.ContainsKey(go) && !CanGrabOnShelf(go)) { lastEvent = $"{go.name} は画面に全部映っていないので取れない"; return; }
        TakeProduct(p, go);
    }

    // 商品を自分の画面へ移し、カーソルも自分の画面へ
    void TakeProduct(Player p, GameObject go)
    {
        // 掴み合いの判定用に、棚での場所を覚えておく
        p.lastTaken = go;
        p.lastTakenTime = Time.time;
        p.lastTakenShelfPos = go.transform.position;
        var cols = go.GetComponentsInChildren<Collider2D>();
        p.lastTakenBounds = cols.Length > 0 ? cols[0].bounds : new Bounds(go.transform.position, Vector3.one);
        foreach (var c in cols) p.lastTakenBounds.Encapsulate(c.bounds);

        SetLayer(go, p.layer);
        var sr = go.GetComponent<SpriteRenderer>();
        sr.color = originalColors[go];
        sr.sortingOrder = 999;   // 持ち運び中は手前に

        // 持ち運び中は物理を止める(他の商品を押しのけない)
        foreach (var c in go.GetComponentsInChildren<Collider2D>()) c.isTrigger = true;
        var rb = go.GetComponent<Rigidbody2D>();
        if (rb == null) rb = go.AddComponent<Rigidbody2D>();
        rb.bodyType = RigidbodyType2D.Kinematic;
        rb.linearVelocity = Vector2.zero;
        rb.angularVelocity = 0f;
        go.transform.rotation = Quaternion.identity;

        // 棚から取ったので、その場所に補充する(1回だけ)
        // (補充しないときも、棚の商品の一覧からは外す。並べ直しで棚へ戻されないように)
        if (slotOf.TryGetValue(go, out var slot))
        {
            slotOf.Remove(go);
            slot.go = null;
            if (UseDeck) { ReturnCard(slot.product); slot.product = -1; }   // 取った商品のカードは山札に戻る
            SetSlotTag(slot);
            if (refillShelf) StartCoroutine(RefillLater(slot, go));
        }

        p.held = go;
        p.ownPos = new Vector2(0f, dropHeight);   // 自分の画面の上のほう(dropHeight)から始める
        p.inShop = false;
        go.transform.position = new Vector3(p.ownPos.x, p.ownPos.y, 0f);

        lastEvent = $"{p.name}が {go.name} を掴んだ → 画面{p.screen + 1}へ移動(Aで離す)";
        Debug.Log($"[Shop2D] {lastEvent}");
    }

    // 自分の画面で持っている商品を、時計回りに90度回す(離したあとも向きはそのまま)
    void RotateHeld(Player p)
    {
        if (!rotateHeldProduct || p.held == null || p.inShop) return;
        p.held.transform.Rotate(0f, 0f, -90f);
        lastEvent = $"{p.name}が {p.held.name} を回した({Mathf.RoundToInt(p.held.transform.eulerAngles.z)}度)";
    }

    void Drop(Player p)
    {
        if (p.held == null) { p.inShop = true; return; }

        var go = p.held;

        // 今のカーソル位置から自由落下させる
        go.transform.position = new Vector3(p.ownPos.x, p.ownPos.y, 0f);
        go.GetComponent<SpriteRenderer>().sortingOrder = ++dropOrder;

        foreach (var c in go.GetComponentsInChildren<Collider2D>()) c.isTrigger = false;
        var rb = go.GetComponent<Rigidbody2D>();
        rb.bodyType = RigidbodyType2D.Dynamic;
        rb.gravityScale = gravityScale;
        rb.linearVelocity = Vector2.zero;
        rb.freezeRotation = freezeRotation;
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;   // 袋が強く揺れても、ふちをすり抜けないように
        // 物理の重さ(mass)は、ふつうは全部 1。商品ごとの重さ(袋が破れる重さ)を mass にすると、重い商品(3)と軽い商品(0.5)の差が大きく、
        // 軽い商品が押されて袋のふちをすり抜けやすくなった。Weight Affects Physics を ON にしたときだけ重さを使う
        rb.mass = weightAffectsPhysics ? Mathf.Max(0.01f, WeightOf(go)) : 1f;

        if (!p.dropped.Contains(go)) p.dropped.Add(go);

        lastEvent = $"{p.name}が {go.name} を離した → 落下 / カーソルは棚へ戻る";
        Debug.Log($"[Shop2D] {lastEvent}");

        p.held = null;
        p.inShop = true;
    }

    // ---------- 画面上の表示 ----------

    void OnGUI()
    {
        if (!showOverlayLabel || switcher == null) return;
        if (textFont != null) GUI.skin.font = textFont;   // 左上の確認用表示も同じフォントに
        // 解像度が高い画面でも読める大きさにする(高さ 1080 を基準)
        float guiScale = Mathf.Max(1f, Screen.height / 1080f) * Mathf.Max(0.5f, textScale / 2f);
        GUI.matrix = Matrix4x4.Scale(new Vector3(guiScale, guiScale, 1f));
        GUI.Label(new Rect(10, 34, 1100, 24), $"{lastEvent}");
        GUI.Label(new Rect(10, 58, 1100, 24), $"接続中のコントローラー: {PadCount}台");
        string wait = HasPadRegistration ? "未接続(登録したコントローラーをつなぐ)" : "未参加(コントローラーのAボタンを押す)";
        GUI.Label(new Rect(10, 82, 1100, 24),
            "1P: " + (pad1 != null ? PadLabel(pad1) : wait) + " / 2P: " + (pad2 != null ? PadLabel(pad2) : wait) +
            (HasPadRegistration ? "(登録済み。F2 かビューボタン長押しで登録し直す)" : "(F2 かビューボタン長押しでコントローラー登録)"));
    }
}