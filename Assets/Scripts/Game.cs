﻿using Cavern;
using System;
using UnityEngine;
using Random = UnityEngine.Random;

namespace LeapSlice {
    /// <summary>
    /// Támogatott vezérlők.
    /// </summary>
    public enum Controllers { Mouse, DualAxis, LeapMotion, HoloLens }
    /// <summary>
    /// Játékmódok.
    /// </summary>
    public enum GameModes { ScoreAttack = 1, TimeAttack, Arcade, Multiplayer }

    /// <summary>
    /// A játékot kezelő fő komponens.
    /// </summary>
    public class Game : Singleton<Game> {
        Controllers PreviousController, CurrentController;

        /// <summary>
        /// Bemenet letiltása ennyi időre, menüváltáskor jól jön.
        /// </summary>
        float DisableInput = .25f;

        [Tooltip("Kiléphetetlen mód, kivonulásokhoz.")]
        public bool KioskMode = false;
        [Tooltip("A HoloLens dolgokat bekapcsolja, de a világot nem állítja be neki.")]
        public bool HoloLensMode = false;

        [Range(.1f, 3)]
        [Tooltip("Objektumok kiemelkedésének szorzója SBS módban.")]
        public float DepthScale = 1;

        [Range(1, 3)]
        [Tooltip("Árkád mód sebességszorzója.")]
        public float ArcadeSpeed = 1.5f;

        [Header("Személyre szabási választék")]
        [Tooltip("Választható pengék anyagmintái.")]
        public Material[] Blades;
        [Tooltip("Választható hátterek textúrái.")]
        public Texture[] Wallpapers;

        [Header("Objektumok és menühöz szükséges dolgok")]
        [Tooltip("Az elsődleges játékos mutatója.")]
        public GameObject Pointer;
        [Tooltip("A többjátékos mód egeres játékosának mutatója.")]
        public GameObject Player2;
        [Tooltip("A játék hátteréül szolgáló fal.")]
        public GameObject Background;

        [Tooltip("Robbanás animáció.")]
        public GameObject Explosion;

        public GameObject[] Objects;
        public GameObject Grenade;

        [Header("Hangok")]
        [Tooltip("Vágáshangok.")]
        public AudioClip[] SliceSounds;
        [Tooltip("Kombók hangja.")]
        public AudioClip ComboSound;
        [Tooltip("Esések hangja.")]
        public AudioClip FallSound;
        [Tooltip("Lelassulás hangja.")]
        public AudioClip SlowMotionInSound;
        [Tooltip("Visszagyorsulás hangja.")]
        public AudioClip SlowMotionOutSound;

        [Header("Power upok jelzői és képernyőeffektjei")]
        public GameObject DoubleScoreMarker;
        public Texture DoubleScoreBackground;
        public GameObject ItemstormMarker;
        public Texture ItemstormBackground;
        public GameObject SlowMotionMarker;
        public Texture SlowMotionBackground;

        public static Vector3 BottomLeft, BottomRight, TopLeft, CloserBL, CloserBR, CloserTL, Forward, ForceLeft, ForceRight;

        /// <summary>
        /// Megnyitott menü.
        /// </summary>
        static int SelectedMenu = -1;
        /// <summary>
        /// Játékmező mérete.
        /// </summary>
        [NonSerialized] public float Scale;
        /// <summary>
        /// Menüelemek.
        /// </summary>
        Menus.Item[] MenuItems = new Menus.Item[0];
        /// <summary>
        /// Billentyűzet / gamepad utolsó pozíciója.
        /// </summary>
        Vector2 AxesPosition = Vector2.zero;
        /// <summary>
        /// Egér utolsó pozíciója a képernyőn.
        /// </summary>
        Vector2 MousePosition = Vector2.zero;
        /// <summary>
        /// LeapMotion által legrégebben érzékelt kéz pozíciója a képernyőn. Ha nincs kéz, (-1, -1).
        /// </summary>
        Vector2 LeapPosition = new Vector2(-1, -1);
        /// <summary>
        /// A falon nézett pont.
        /// </summary>
        Vector3 LookPoint = Vector3.zero, LookPointP2 = Vector3.zero;

        /// <summary>
        /// Játék előkészítése, elemek skálázása
        /// </summary>
        void Start() {
            PreviousController = CurrentController = Controllers.Mouse;
            MousePosition = Input.mousePosition;
            SetBlade(PlayerPrefs.GetInt("Blade", 0));
            SetBackground(PlayerPrefs.GetInt("Wallpaper", 0));
            for (int i = 0; i < (int)GameModes.Multiplayer; ++i)
                TopScores[i] = PlayerPrefs.GetInt("Top" + i, 0);
            BestCombo = PlayerPrefs.GetInt("BestCombo", 0);
            Scale = Background.transform.localScale.x;
            if (HoloLensMode) {
                Background.transform.localScale = new Vector3(Scale, Scale * .4f, 1);
                PreviousController = CurrentController = Controllers.HoloLens;
                BottomLeft = Background.transform.position + Background.transform.rotation * new Vector3(Scale * -.5f, Scale * -.2f, Scale * -.01f);
                BottomRight = BottomLeft + Background.transform.rotation * new Vector3(Scale, 0, 0);
                TopLeft = BottomLeft + new Vector3(0, Scale * .4f, 0);
                Forward = Quaternion.Euler(0, -90, 0) * (BottomRight - BottomLeft).normalized * (Scale * .06f) * DepthScale;
            } else {
                BottomLeft = Utils.ScreenPosInWorld(new Vector2(0, 0));
                BottomRight = Utils.ScreenPosInWorld(new Vector2(Screen.width, 0));
                TopLeft = Utils.ScreenPosInWorld(new Vector2(0, Screen.height));
                Forward = Camera.main.transform.forward * Scale * .2f * DepthScale;
            }
            float NormalScale = Scale * .02f;
            Pointer.GetComponent<TrailRenderer>().startWidth *= NormalScale;
            Player2.GetComponent<TrailRenderer>().startWidth *= NormalScale;
            Physics.gravity *= NormalScale;
            CloserBL = BottomLeft - Forward;
            CloserBR = BottomRight - Forward;
            CloserTL = TopLeft - Forward;
            ForceRight = (BottomRight - BottomLeft).normalized * (Scale + Scale);
            ForceLeft = -ForceRight;
            AudioListener3D.EnvironmentSize *= 2;
            SpawnMainMenu();
        }

        /// <summary>
        /// Véletlenszerű tárgy a bedobálhatóak közül, árkád módban power up is lehet.
        /// </summary>
        /// <param name="MenuItem">Ha menüelem helyén lesz, akkor annak az azonosítója</param>
        public GameObject RandomObject(int MenuItem = -1) {
            GameObject Obj = Instantiate(Objects[Random.Range(0, Objects.Length)]); // Tárgy kiválasztása
            if (GameMode == GameModes.Arcade && Random.value < .1f) { // Árkád módban 10% eséllyel power up
                int PowerUpKind = Random.Range(0, 3);
                Obj.GetComponent<Target>().PowerUpKind = (PowerUps)PowerUpKind;
                GameObject Aura = null; // Aura létrehozása az objektum köré
                switch (PowerUpKind) {
                    case (int)PowerUps.DoubleScore: Aura = Instantiate(DoubleScoreMarker); break;
                    case (int)PowerUps.Itemstorm: Aura = Instantiate(ItemstormMarker); break;
                    default: Aura = Instantiate(SlowMotionMarker); break;
                }
                Aura.transform.position = Obj.transform.position;
                Aura.transform.parent = Obj.transform;
            }
            Obj.transform.localScale *= Scale * .02f;
            if (MenuItem != -1) {
                Obj.name = "Menu" + MenuItem;
                Obj.GetComponent<Rigidbody>().useGravity = false;
            }
            return Obj;
        }

        /// <summary>
        /// Helyes méretre skálázott gránát létrehozása.
        /// </summary>
        public GameObject ScaledGrenade() {
            GameObject Obj = Instantiate(Grenade);
            Obj.transform.localScale *= Scale * .02f;
            return Obj;
        }

        /// <summary>
        /// Elsődleges játékos pengéjének módosítása.
        /// </summary>
        int SetBlade(int N) { Pointer.GetComponent<Renderer>().material = Blades[N]; return N; }
        /// <summary>
        /// Háttér módosítása.
        /// </summary>
        int SetBackground(int N) { Background.GetComponent<Renderer>().material.mainTexture = Wallpapers[N]; return N; }

        /// <summary>
        /// Menüképernyők előkészítése.
        /// </summary>
        /// <param name="N">Elemszám</param>
        /// <param name="Partial">Ha csak egyetlen elemet kell frissíteni, akkor -1 helyett annak a helye a MenuItems tömbben</param>
        void RenewMenu(int N, int Partial = -1) {
            if (Partial != -1) {
                MenuItems[Partial].Obj = RandomObject(Partial);
                return;
            }
            Menus.DespawnMenu(ref MenuItems);
            MenuItems = new Menus.Item[N];
            for (int i = 0; i < N; ++i)
                MenuItems[i].Obj = RandomObject(i);
        }

        /// <summary>
        /// Főmenü megjelenítése.
        /// </summary>
        void SpawnMainMenu() {
            SelectedMenu = 0;
            RenewMenu(!KioskMode ? 3 : 2);
            Menus.SetupMainMenu(MenuItems);
        }

        /// <summary>
        /// Személyreszabási képernyő megjelenítése.
        /// </summary>
        /// <param name="Partial">Ha csak egyetlen elemet kell frissíteni, akkor -1 helyett annak a helye a MenuItems tömbben</param>
        void SpawnCustomizationMenu(int Partial = -1) {
            SelectedMenu = 1;
            RenewMenu(8, Partial);
            int BestScore = 0;
            for (int i = 0; i < (int)GameModes.Multiplayer; ++i)
                BestScore = Math.Max(BestScore, TopScores[i]);
            Menus.SetupCustomizationMenu(MenuItems, BestScore, BestCombo);
        }

        /// <summary>
        /// Módválasztó képernyő megjelenítése.
        /// </summary>
        void SpawnModeSelect() {
            SelectedMenu = 2;
            RenewMenu(4 + Convert.ToInt32(!HoloLensMode));
            Menus.SetupModeSelect(MenuItems);
        }

        /// <summary>
        /// Játék végének kezelése, és a játék vége képernyő megjelenítése.
        /// </summary>
        void GameOver() {
            Playing = false;
            Time.timeScale = 1; // Játék közben az idő felgyorsul, ez visszaállítja
            DisableInput = 1; // Nagyon gyakori valamelyik gomb átvágása a játék utáni kapkodásban a képernyő alján, 1 másodperc elég a játékosnak felfogni, hogy már nem lehet
            if (GameMode != GameModes.Multiplayer) { // Többjátékos módon kívül egyszerűen a játékmódnak megfelelő helyen van a rekord
                if (TopScores[(int)GameMode] < Score) TopScores[(int)GameMode] = Score;
            } else { // A többjátékos módé a 0. hely
                if (TopScores[0] < Score) TopScores[0] = Score;
                if (TopScores[0] < ScoreP2) TopScores[0] = ScoreP2;
            }
            SelectedMenu = 3;
            RenewMenu(2);
            Menus.SetupGameOver(MenuItems);
        }

        /// <summary>
        /// Új játék indítása.
        /// </summary>
        /// <param name="Mode">Új játékmód beállítása, amennyiben van értéke</param>
        void LaunchGame(GameModes? Mode = null) {
            if (Mode.HasValue)
                GameMode = Mode.Value;
            Menus.DespawnMenu(ref MenuItems);
            Dispenser.DispenserReset();
            Playing = true;
            Score = 0;
            ThisCombo = 0;
            SinceLastCombo = 0;
            if (GameMode == GameModes.Arcade) // Induljon hiperaktívan az árkád mód
                Time.timeScale = ArcadeSpeed;
            Lives = 3; // Score attack / multiplayer
            TimeRemaining = 60; // Time attack / arcade
            ScoreP2 = 0; // Multiplayer
            CurrentController = Controllers.LeapMotion + Convert.ToInt32(HoloLensMode) * (Controllers.HoloLens - Controllers.LeapMotion);
        }

        /// <summary>
        /// Játék van-e folyamatban.
        /// </summary>
        public static bool Playing = false;
        /// <summary>
        /// Pontszám.
        /// </summary>
        public static int Score, ScoreP2;
        /// <summary>
        /// A játékos(ok) életei.
        /// </summary>
        int Lives;
        /// <summary>
        /// Időkorlátos játékmódokból hátralévő idő.
        /// </summary>
        float TimeRemaining;
        /// <summary>
        /// A játékos összesített legjobb kombója.
        /// </summary>
        int BestCombo;
        /// <summary>
        /// Rekordok a játékmódokhoz: mivel 1-től indexelődnek, és a multiplayer az utolsó, akkora kell legyen a tömb.
        /// </summary>
        public static int[] TopScores = new int[(int)GameModes.Multiplayer];
        /// <summary>
        /// Jelenlegi kombó mérete.
        /// </summary>
        public static int ThisCombo;
        /// <summary>
        /// A jelenlegi kombó utolsó vágása óta eltelt idő.
        /// </summary>
        public static float SinceLastCombo;
        /// <summary>
        /// Az utoljára elvágott tárgy pozíciója. Ha kombó történt, ott jelezze.
        /// </summary>
        public static Vector3 LastHit;
        /// <summary>
        /// A jelenlegi játékmód.
        /// </summary>
        public static GameModes GameMode;

        /// <summary>
        /// Életvesztéskor hívódik.
        /// </summary>
        public void LoseLife() {
            if ((GameMode == GameModes.ScoreAttack || GameMode == GameModes.Multiplayer) && --Lives == 0)
                GameOver();
        }

        /// <summary>
        /// A felhasználói felület képernyőre rajzolt része.
        /// </summary>
        void OnGUI() {
            int ScrW = Screen.width, ScrX = !SBS.Enabled ? ScrW : ScrW / 2, ScrY = Screen.height;
            float BoxW = ScrX / 5, BoxH = ScrX / 24, HBoxW = BoxW / 2;
            GUI.skin.box.fontSize = ScrX / 32;
            foreach (Menus.Item Item in MenuItems) {
                float MarginLeft = Item.ActualScrPos.x - HBoxW;
                GUI.Box(new Rect(MarginLeft, ScrY - Item.ActualScrPos.y + ScrY / 15, BoxW, BoxH), Item.Action);
                if (SBS.Enabled)
                    GUI.Box(new Rect(MarginLeft > ScrX ? MarginLeft - ScrX : MarginLeft + ScrX, ScrY - Item.ActualScrPos.y + ScrY / 15, BoxW, BoxH), Item.Action);
            }
            if (Playing) { // Játék
                int Margin = ScrY / 20;
                GUI.skin.label.alignment = TextAnchor.UpperRight;
                GUI.skin.label.fontSize = ScrY / 10;
                Rect RightRect = new Rect(0, Margin, ScrX - Margin, ScrY);
                if (GameMode == GameModes.ScoreAttack)
                    Utils.ShadedLabel(RightRect, Lives == 3 ? "♥♥♥" : Lives == 2 ? "♥♥" : "♥", Color.red);
                else if (GameMode == GameModes.Multiplayer)
                    Utils.ShadedLabel(RightRect, ScoreP2.ToString(), Color.blue);
                else
                    Utils.ShadedLabel(RightRect, Mathf.CeilToInt(TimeRemaining).ToString(), Color.red);
                GUI.color = Color.green;
                if (GameMode == GameModes.Multiplayer) {
                    GUI.skin.label.alignment = TextAnchor.UpperCenter;
                    Utils.ShadedLabel(new Rect(0, Margin, ScrX, ScrY), Lives == 3 ? "♥♥♥" : Lives == 2 ? "♥♥" : "♥", Color.red);
                }
                GUI.skin.label.alignment = TextAnchor.UpperLeft;
                Utils.ShadedLabel(new Rect(Margin, Margin, ScrX, ScrY), Score.ToString(), Color.green);
                if (GameMode != GameModes.Multiplayer) {
                    int TopScore = TopScores[(int)GameMode];
                    if (TopScore != 0) {
                        int HighTop = Margin + GUI.skin.label.fontSize;
                        GUI.skin.label.fontSize /= 2;
                        Utils.ShadedLabel(new Rect(Margin, HighTop, ScrX, ScrY), "Best: " + (TopScore > Score ? TopScore : Score), TopScore < Score ? Color.green : Color.white);
                    }
                }
                GUI.color = Color.white;
            } else if (SelectedMenu == 3) { // Game over
                GUI.skin.label.alignment = TextAnchor.UpperCenter;
                int Top = GUI.skin.label.fontSize = ScrY / 10, TopScore;
                if (GameMode != GameModes.Multiplayer) {
                    Utils.ShadedLabel(new Rect(0, Top += GUI.skin.label.fontSize, ScrX, ScrY), GameMode == GameModes.ScoreAttack ? "Game Over" : "Time Up", Color.white);
                    TopScore = TopScores[(int)GameMode];
                    if (Score != TopScore)
                        Utils.ShadedLabel(new Rect(0, Top += GUI.skin.label.fontSize, ScrX, ScrY), "High score: " + TopScore, Color.white);
                    GUI.skin.label.fontSize = ScrY / 8;
                    Utils.ShadedLabel(new Rect(0, Top += GUI.skin.label.fontSize, ScrX, ScrY),
                        (Score == TopScore ? "New high score: " : "Score: ") + Score, Score == TopScore ? Color.green : Color.red);
                } else {
                    Utils.ShadedLabel(new Rect(0, Top += GUI.skin.label.fontSize, ScrX, ScrY), Score == ScoreP2 ? "Draw" : Score > ScoreP2 ? "Player 1 wins" : "Player 2 wins", Color.white);
                    GUI.skin.label.fontSize = ScrY / 8;
                    Utils.ShadedLabel(new Rect(0, Top += GUI.skin.label.fontSize, ScrX, ScrY),
                        "P1 : " + Score + " | " + ScoreP2 + " : P2", Color.blue);
                }
            } else if (SelectedMenu == 0) { // Főmenü
                Rect InfoRect = new Rect(Screen.width - 30, 5, 20, 20);
                GUI.Button(InfoRect, "?");
                Vector2 MousePos = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
                if (InfoRect.Contains(MousePos)) {
                    GUI.skin.box.fontSize = 12;
                    GUI.Box(new Rect(MousePos.x - 200, MousePos.y, 200, 65), "LEGO kör - http://lego.sch.bme.hu\nBence Sgánetz\nÁdám Szi\nModels by Ákos Tóth");
                }
            }
        }

        public void OnMenuItemSelected(int ID) {
            switch (SelectedMenu) {
                case 0: // Főmenü
                    switch (ID) {
                        case 0: SpawnModeSelect(); break; // Új játék
                        case 1: SpawnCustomizationMenu(); break; // Testreszabás
                        case 2: Application.Quit(); return; // Kilépés
                    }
                    break;
                case 1: // Testreszabás
                    int BladeCount = Blades.Length;
                    if (ID == BladeCount + Wallpapers.Length) // Vissza
                        SpawnMainMenu();
                    else if (ID != -1) { // Kiválasztott elemek
                        if (ID < BladeCount) {
                            if (!MenuItems[ID].Action.StartsWith("Reach"))
                                PlayerPrefs.SetInt("Blade", SetBlade(ID));
                        } else if (!MenuItems[ID].Action.StartsWith("Combo"))
                                PlayerPrefs.SetInt("Wallpaper", SetBackground(ID - BladeCount));
                        SpawnCustomizationMenu(ID < BladeCount + Wallpapers.Length ? ID : -1);
                    }
                    break;
                case 2: // Játékmódválasztó
                    if (ID == 0)
                        SpawnMainMenu();
                    else
                        LaunchGame((GameModes)ID);
                    break;
                case 3: // Game Over
                    if (ID == 0) // Újra
                        LaunchGame();
                    else { // Főmenü
                        GameMode = GameModes.ScoreAttack; // Ha többjátékos lenne, használható lenne a player 2 pointer, árkádban pedig powerupok kerülnének a menübe
                        SpawnMainMenu(); // Menü megjelenítése
                    }
                    break;
                default:
                    break;
            }
            DisableInput = .25f;
        }

        /// <summary>
        /// Képkockafrissítés.
        /// </summary>
        void Update() {
            // Játék kezelése
            if (Playing) {
                Dispenser.DispenserUpdate(); // Dobáló frissítése
                // Kombó kiértékelése
                SinceLastCombo += Time.deltaTime;
                if (SinceLastCombo > .15f && GameMode != GameModes.Multiplayer) {
                    if (ThisCombo > 2) {
                        if (BestCombo < ThisCombo)
                            BestCombo = ThisCombo;
                        GameObject Marker = new GameObject();
                        Marker.transform.position = LastHit;
                        ComboMarker ComboMark = Marker.AddComponent<ComboMarker>();
                        ComboMark.Combo = ThisCombo;
                        ComboMark.Duration *= Time.timeScale;
                        Score += ThisCombo;
                    }
                    ThisCombo = 0;
                }
                // Idő elleni módok
                if (GameMode == GameModes.TimeAttack || GameMode == GameModes.Arcade) {
                    TimeRemaining -= Time.deltaTime / Time.timeScale;
                    if (TimeRemaining <= 0)
                        GameOver();
                }
                if (Input.GetKeyDown(KeyCode.Escape)) // Esc-re megállás
                    GameOver();
            }
            // Menüobjektumok elhelyezése
            Menus.PlaceMenu(MenuItems);
            // Kurzor helye a kijelzőn és a világban
            Vector2 LookPosition = new Vector2(-1, -1), OldPosition = Vector2.zero, OldP2Position = Vector2.zero;
            switch (PreviousController) {
                case Controllers.Mouse:
                    OldPosition = MousePosition;
                    LookPosition = MousePosition = Input.mousePosition;
                    SwitchController();
                    break;
                case Controllers.DualAxis:
                    OldPosition = AxesPosition;
                    float ScreenHeight = Screen.height, TimeScale = 10 * Time.deltaTime;
                    LookPosition = AxesPosition = new Vector2() {
                        x = Mathf.Clamp(AxesPosition.x + Input.GetAxis("Horizontal") * ScreenHeight * TimeScale, 0, Screen.width),
                        y = Mathf.Clamp(AxesPosition.y + Input.GetAxis("Vertical") * ScreenHeight * TimeScale, 0, ScreenHeight)
                    };
                    SwitchController();
                    break;
                case Controllers.LeapMotion:
                    LeapMotion Leap = LeapMotion.Instance;
                    int HalfLeapSpaceHeight = (int)(Screen.height * (Leap.LeapUpperBounds.x - Leap.LeapLowerBounds.x) / Screen.width) / 2;
                    Leap.LeapLowerBounds.y = 200 - HalfLeapSpaceHeight;
                    Leap.LeapUpperBounds.y = 200 + HalfLeapSpaceHeight;
                    int ExtendedFingers = Leap.ExtendedFingers();
                    if (Leap.GetHandCount() == 0) {
                        DisableInput = .125f;
                    } else if (ExtendedFingers != 0) {
                        OldPosition = LeapPosition;
                        LookPosition = LeapPosition = ExtendedFingers == 1 ? Leap.SinglePointOnScreen() : Leap.PalmOnScreen();
                    }
                    if (GameMode == GameModes.Multiplayer) {
                        OldP2Position = MousePosition;
                        MousePosition = Input.mousePosition;
                    } else
                        SwitchController();
                    break;
                case Controllers.HoloLens:
                    OldPosition = LookPosition = new Vector2(Screen.width / 2, Screen.height / 2);
                    break;
            }
            if (PreviousController != CurrentController || (LookPosition.x == -1 && LookPosition.y == -1)) {
                OldPosition = LookPosition; // Ha bármiféle eszközcsere/inputkiesés történt, ne vágjon semmit...
                Pointer.GetComponent<TrailRenderer>().Clear(); // ...és az előző vágási nyomvonal tűnjön el, mivel nem az folytatódik már
            }
            PreviousController = CurrentController;
            PointerHandler(ref Pointer, ref OldPosition, ref LookPosition, ref Score, ref LookPoint);
            if (GameMode == GameModes.Multiplayer)
                PointerHandler(ref Player2, ref OldP2Position, ref MousePosition, ref ScoreP2, ref LookPointP2);
        }

        /// <summary>
        /// Váltson arra a vezérlőre, ahonnan érkezik bemenet.
        /// </summary>
        void SwitchController() {
            if (CurrentController != Controllers.Mouse && (MousePosition.x != Input.mousePosition.x || MousePosition.y != Input.mousePosition.y)) { // Ha megmozdul az egér, váltson rá
                CurrentController = Controllers.Mouse;
                return;
            }
            if (CurrentController != Controllers.DualAxis && (Input.GetAxis("Horizontal") != 0 || Input.GetAxis("Vertical") != 0)) { // Ha bemenet érkezik billentyűzetről / gamepadről
                CurrentController = Controllers.DualAxis;
                return;
            }
            if (CurrentController != Controllers.LeapMotion && LeapMotion.Instance.SinglePointOnScreen().x != -1) { // Ha megmozdul a LeapMotion, váltson rá
                CurrentController = Controllers.LeapMotion;
                return;
            }
        }

        /// <summary>
        /// Egy játékos mutatójának kezelése.
        /// </summary>
        /// <param name="PointerObj">Mutatóobjektum</param>
        /// <param name="OldPosition">Előző pozíció a képernyőn</param>
        /// <param name="LookPosition">Jelenlegi pozíció a képernyőn</param>
        /// <param name="InScore">Játékos pontszáma</param>
        /// <param name="LookingPoint">Nézési pont (kizárólag ez a függvény kezelje, és Vector3.zero-ra legyen inicializálva)</param>
        void PointerHandler(ref GameObject PointerObj, ref Vector2 OldPosition, ref Vector2 LookPosition, ref int InScore, ref Vector3 LookingPoint) {
            if (DisableInput > 0)
                DisableInput -= Time.deltaTime;
            Vector3 OldPoint = LookingPoint;
            LookingPoint = Utils.ScreenPosInWorld(LookPosition);
            // Elvágott tárgyak megtalálása - minden mást azok csinálnak
            float SwipeSpeed = Mathf.Sqrt(Utils.Square(LookingPoint.x - OldPoint.x) + Utils.Square(LookingPoint.y - OldPoint.y)) / Screen.width;
            for (int Substep = 0; Substep < 100; Substep++) { // 100-szoros részletességgel nézze át a megtett utat
                Ray ray = Camera.main.ScreenPointToRay(Vector2.LerpUnclamped(OldPosition, LookPosition, Substep / 100f));
                RaycastHit Hit;
                if (Physics.Raycast(ray, out Hit)) {
                    if (SwipeSpeed > Scale * .0000025f) {
                        Target HitTarget = Hit.collider.gameObject.GetComponent<Target>();
                        if (HitTarget && DisableInput <= 0)
                            HitTarget.Hit(ref InScore);
                    }
                }
            }
            PointerObj.transform.position = LookingPoint - Forward * .8f;
        }

        /// <summary>
        /// Mentés kilépéskor.
        /// </summary>
        void OnApplicationQuit() {
            for (int i = 0; i < (int)GameModes.Multiplayer; ++i)
                PlayerPrefs.SetInt("Top" + i, TopScores[i]);
            PlayerPrefs.SetInt("BestCombo", BestCombo);
            PlayerPrefs.Save();
        }
    }
}