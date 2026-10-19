#if UNITY_EDITOR || GPIE_DEVELOPMENT
using System.Collections.Generic;
using Gameplay.View;
using UnityEngine;

namespace Gameplay.Dev
{
    /// <summary>
    /// Панель настройки деревянного стиля ПРЯМО В PLAY: углы, волокно, свет, цвета, тень.
    /// Владелец просил крутить лайфтайм — до этого любая правка требовала пересборки стенда.
    ///
    /// IMGUI намеренно: сцены здесь собираются кодом, префабов под UI нет, а IMGUI не нужен ни
    /// канвас, ни EventSystem — панель поднимается сама в любой сцене и ничего в ней не занимает.
    /// Клавиша <b>T</b> или кнопка «ДЕРЕВО» в углу — показать/скрыть.
    ///
    /// В УРОВНЯХ ПАНЕЛИ НЕТ. Раньше она поднималась сама на входе в Play, и кнопка «ДЕРЕВО (T)»
    /// висела поверх игры — владелец отдаёт билд, и МОЕЙ дебажной кнопки там быть не должно
    /// (штатное дев-меню «DEV» это не касается, оно на месте). Панель создаётся только тем, кто её
    /// просит: песочница и каталог деталей через <see cref="Ensure"/>. Вернуть в уровни —
    /// <c>[RuntimeInitializeOnLoadMethod]</c> с вызовом <see cref="Ensure"/>.
    ///
    /// Живёт только в редакторе и dev-сборках (весь файл под <c>#if</c>), как и
    /// <see cref="BringupDebugMenuHost"/>.
    ///
    /// ОДИН ПОДВОХ: ввод геймплея в bringup идёт мимо uGUI (сырой PointerTapInputSource), поэтому
    /// тап по панели в НАСТОЯЩЕМ уровне заодно роняет деталь. В песочнице геймплея нет, там это
    /// безразлично; в уровне панель лучше открывать, когда деталь уже упала.
    /// </summary>
    public sealed class WoodTunerPanel : MonoBehaviour
    {
        private enum Tab { Деталь, Форма, Волокно, Свет, Цвета, Стейты, Тень, Воздух }

        private static WoodTunerPanel _instance;

        public static WoodTunerPanel Ensure()
        {
            if (_instance != null)
                return _instance;
            var go = new GameObject("[Dev] WoodTunerPanel");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<WoodTunerPanel>();
            return _instance;
        }

        /// <summary>Показана ли панель. Песочница включает её сразу, в остальных сценах — по кнопке.</summary>
        public bool Visible;

        private Tab _tab = Tab.Форма;
        private Vector2 _scroll;
        private int _selectedColor;
        private string _status;
        private float _statusUntil;

        private GUIStyle _label, _title, _small;
        private WoodSandbox _sandbox;
        private readonly Dictionary<string, Texture2D> _swatches = new Dictionary<string, Texture2D>();

        private void Update()
        {
            // Клавиатура читается старым Input только когда он включён; в проекте активен новый
            // Input System, поэтому идём через него. Пакет здесь есть (его использует BringupDebugMenuHost).
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (keyboard != null && keyboard.tKey.wasPressedThisFrame)
                Visible = !Visible;
        }

        private void OnGUI()
        {
            EnsureStyles();

            // Экран телефона высокий и плотный: без масштабирования IMGUI получается нечитаемо мелким.
            var scale = Mathf.Clamp(Screen.height / 1000f, 1f, 3f);
            var prevMatrix = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            var w = Screen.width / scale;
            var h = Screen.height / scale;

            if (!Visible)
            {
                if (GUI.Button(new Rect(8f, h - 46f, 130f, 38f), "ДЕРЕВО (T)"))
                    Visible = true;
                GUI.matrix = prevMatrix;
                return;
            }

            var panel = new Rect(w - 396f, 8f, 388f, h - 16f);
            GUI.Box(panel, GUIContent.none);
            GUILayout.BeginArea(new Rect(panel.x + 10f, panel.y + 8f, panel.width - 20f, panel.height - 16f));

            DrawHeader();
            DrawTabs();

            _scroll = GUILayout.BeginScrollView(_scroll);
            var tuning = WoodTuning.Current;
            var changed = false;
            var shapeChanged = false;

            switch (_tab)
            {
                case Tab.Деталь: DrawPiece(tuning, ref shapeChanged); break;
                case Tab.Форма: DrawShape(tuning, ref shapeChanged); break;
                case Tab.Волокно: changed = DrawGrain(tuning); break;
                case Tab.Свет: changed = DrawLight(tuning); break;
                case Tab.Цвета: changed = DrawColors(tuning); break;
                case Tab.Стейты: changed = DrawStates(tuning); break;
                case Tab.Тень: changed = DrawShadow(tuning); break;
                case Tab.Воздух: changed = DrawAir(tuning); break;
            }

            GUILayout.EndScrollView();
            GUILayout.EndArea();
            GUI.matrix = prevMatrix;

            // Пересборка мешей заметно дороже правки материала, поэтому только когда трогали форму.
            if (shapeChanged)
                tuning.Apply();
            else if (changed)
                tuning.Apply(rebuildMeshes: false);
        }

        // ------------------------------------------------------------------ шапка

        private void DrawHeader()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("ДЕРЕВО — настройка", _title);
            if (GUILayout.Button("скрыть", GUILayout.Width(70f)))
                Visible = false;
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Сохранить"))
                Say($"сохранено: {WoodTuning.Current.Save()}");
            if (GUILayout.Button("Сброс"))
            {
                WoodTuning.ResetToDefaults();
                Say("вернул заводские числа");
            }
            if (GUILayout.Button("В консоль"))
            {
                Debug.Log("[wood] настройки:\n" + WoodTuning.Current.ToJson());
                Say("напечатал в консоль");
            }
            GUILayout.EndHorizontal();

            // Обмен настройками текстом: владелец копирует весь блок и отдаёт его мне, я возвращаю
            // правленый — и он вставляет обратно, не выходя из Play. Через буфер обмена, а не файл:
            // на устройстве в ассеты писать нечем, а буфер есть везде.
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Копировать настройки"))
            {
                GUIUtility.systemCopyBuffer = WoodTuning.Current.ToJson();
                Say("весь блок в буфере — можно отдавать");
            }
            if (GUILayout.Button("Вставить"))
            {
                if (WoodTuning.TryPasteFromClipboard(out var error))
                    Say("вставил и применил");
                else
                    Say("не вставилось: " + error);
            }
            GUILayout.EndHorizontal();

            if (_status != null && Time.realtimeSinceStartup < _statusUntil)
                GUILayout.Label(_status, _small);
            else
                GUILayout.Label(" ", _small);
        }

        private void DrawTabs()
        {
            GUILayout.BeginHorizontal();
            foreach (Tab tab in System.Enum.GetValues(typeof(Tab)))
            {
                var on = _tab == tab;
                if (GUILayout.Toggle(on, tab.ToString(), GUI.skin.button) && !on)
                    _tab = tab;
            }
            GUILayout.EndHorizontal();
            GUILayout.Space(6f);
        }

        // ------------------------------------------------------------------ вкладки

        private WoodCatalogSandbox _catalog;

        /// <summary>
        /// Вкладка ВЫБРАННОЙ детали: своя настройка одной формы поверх общих чисел.
        /// Работает в каталоге (<see cref="WoodCatalogSandbox"/>) — там детали кликабельны.
        ///
        /// Настройка вешается на ФОРМУ, а не на «уровень/kind»: квадрат 0.40×0.40 живёт в четырёх
        /// уровнях, и настраивать его четырежды означало бы гарантированно их рассинхронить.
        /// Поэтому здесь же печатается, где ещё эта форма встречается.
        /// </summary>
        private void DrawPiece(WoodTuning t, ref bool shapeChanged)
        {
            if (_catalog == null)
                _catalog = FindFirstObjectByType<WoodCatalogSandbox>();

            if (_catalog == null)
            {
                Hint("Каталог не открыт. Tools → Wood Style → Каталог деталей ▶ — там все детали " +
                     "всех уровней в настоящих размерах, по клику выбирается деталь.");
                return;
            }

            // Листалка страниц: страница = уровень.
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("◀", GUILayout.Width(44f)))
                _catalog.PrevPage();
            GUILayout.Label($"{_catalog.PageName}  [{_catalog.PageIndex + 1}/{_catalog.PageCount}]",
                _label, GUILayout.Width(210f));
            if (GUILayout.Button("▶", GUILayout.Width(44f)))
                _catalog.NextPage();
            GUILayout.EndHorizontal();

            // Собранная фигура — это и есть «левел дизайн»: детали стоят вплотную, и материал там
            // читается иначе, чем в россыпи (работают контур и фаска между соседями).
            if (GUILayout.Button(_catalog.Assembled
                    ? "показано: УРОВЕНЬ  →  показать детали"
                    : "показано: детали  →  показать УРОВЕНЬ"))
                _catalog.ToggleAssembled();

            GUILayout.Space(8f);

            var key = _catalog.SelectedKey;
            if (string.IsNullOrEmpty(key))
            {
                Hint("Ткни в деталь на стенде — она обведётся жёлтым, и здесь появятся её числа.");
                return;
            }

            GUILayout.Label(_catalog.SelectedLabel, _label);
            var usedBy = _catalog.SelectedUsedBy;
            if (usedBy.Count > 1)
                Hint($"Эта же форма ещё в: {string.Join(", ", usedBy)}. Настройка одна на всех — " +
                     "иначе один и тот же кубик разъехался бы по уровням.");

            var o = t.FindOverride(key);
            var has = o != null;
            var wants = GUILayout.Toggle(has, " своя настройка у этой формы");
            if (wants != has)
            {
                if (wants)
                    t.CreateOverride(key);
                else
                    t.RemoveOverride(key);
                shapeChanged = true;
                Say(wants ? "форма отвязана от общих чисел" : "форма вернулась к общим числам");
                return;
            }

            if (!has)
            {
                Hint("Сейчас деталь живёт по ОБЩИМ числам (вкладки Форма/Волокно/Свет). Включи " +
                     "тумблер выше, если этой форме нужно что-то своё — она отпочкуется от текущих " +
                     "значений, а не обнулится.");
                return;
            }

            GUILayout.Space(6f);
            shapeChanged |= Row("Масштаб рисунка", ref o.StyleScale, 0.15f, 1.5f);
            shapeChanged |= Row("Скругление углов", ref o.CornerRadius, 0f, 0.3f);

            GUILayout.Space(6f);
            GUILayout.Label("Волокно", _label);
            shapeChanged |= Row("Линий на опорную", ref o.GrainScale, 1f, 12f);
            shapeChanged |= Row("Угол (град.)", ref o.GrainAngle, -180f, 180f);
            shapeChanged |= Row("Вдоль длинной стороны", ref o.GrainAlignLong, 0f, 1f);

            GUILayout.Space(6f);
            GUILayout.Label("Кромка", _label);
            shapeChanged |= Row("Фаска: ширина", ref o.BevelWidth, 0f, 0.4f);
            shapeChanged |= Row("Фаска: свет", ref o.BevelLight, 0f, 1f);
            shapeChanged |= Row("Фаска: тень", ref o.BevelDark, 0f, 1f);
            shapeChanged |= Row("Кант: ширина", ref o.RimWidth, 0f, 0.15f);
            shapeChanged |= Row("Кант: свет", ref o.RimLight, 0f, 1f);
            shapeChanged |= Row("Кант: тень", ref o.RimDark, 0f, 1f);
            shapeChanged |= Row("Контур: ширина", ref o.LineWidth, 0f, 0.08f);
            shapeChanged |= Row("Контур: сила", ref o.LineDark, 0f, 1f);
            shapeChanged |= Row("Затемнение к краям", ref o.Ao, 0f, 0.6f);
            shapeChanged |= Row("Его глубина", ref o.AoWidth, 0.02f, 1f);

            GUILayout.Space(6f);
            GUILayout.Label("Цвет", _label);
            shapeChanged |= Row("Насыщенность", ref o.Saturation, 0.5f, 2f);
            shapeChanged |= Row("Яркость", ref o.Brightness, 0.5f, 1.5f);

            GUILayout.Space(8f);
            var count = t.PieceOverrides == null ? 0 : t.PieceOverrides.Count;
            Hint($"Своих настроек всего: {count}. В файл попадают только они — остальные формы " +
                 "продолжают слушать общие числа.");
        }

        private void DrawShape(WoodTuning t, ref bool shapeChanged)
        {
            Hint("Масштаб рисунка — ОДИН на всю сцену: доска одна, куски из неё разного размера. " +
                 "1.0 — числа, настроенные в песочнице на квадратах 1.0; 0.4 — те же пропорции в " +
                 "масштабе медианной детали кампании (короткая сторона 0.40 при разбросе 0.18..1.60).");
            shapeChanged |= Row("Масштаб рисунка дерева", ref t.StyleScale, 0.15f, 1.5f);

            GUILayout.Space(6f);
            Hint("Ниже — поправка по размеру КАЖДОЙ детали. ДЕРЖАТЬ В НУЛЕ: на единице у крупного " +
                 "куска выходят три жирные прожилки и широкий белый поясок, у мелкого соседа — " +
                 "частая рябь и тонкий кант. Это и есть «у каждого куска своя доска».");
            shapeChanged |= Row("Опорный размер детали", ref t.DetailRefSize, 0.2f, 2f);
            shapeChanged |= Row("Толщины следуют размеру детали", ref t.DetailScale, 0f, 1f);

            GUILayout.Space(6f);
            var c2 = false;
            var asWood = GUILayout.Toggle(t.StaticAsWood, " пол и стены — деревом, а не спрайтом полки");
            if (asWood != t.StaticAsWood)
            {
                t.StaticAsWood = asWood;
                shapeChanged = true;
                Say(asWood ? "статика пошла через дерево" : "вернул спрайт полки");
            }
            Hint(t.StaticAsWood
                ? "Пол, стена, колонна и штыри рисуются тем же шейдером, что детали, — их слушают все " +
                  "ползунки и палитра. Каждую можно выбрать во вкладке Деталь."
                : "Любой прямоугольный контур подменяется 9-slice спрайтом полки. Один спрайт на всё: " +
                  "и на полку 3.40×0.30, и на стену 2.60×3.90, и на штырь 0.10×0.35. Настройки на них " +
                  "не действуют. Пересоздание сцены (смена страницы каталога) обязательно.");

            GUILayout.Space(6f);
            c2 |= Row("Размер рамки превью", ref t.PreviewScale, 0.2f, 1.5f);
            Hint("Потолок размера карточки сверху. Если она не влезает в верхнюю полосу кадра, вью " +
                 "ужмёт её сам. Применяется при перезагрузке уровня.");

            GUILayout.Space(6f);
            Hint("Углы режутся в геометрии детали, поэтому меши пересобираются на ходу.");
            shapeChanged |= Row("Скругление углов", ref t.CornerRadius, 0f, 0.3f);
            shapeChanged |= RowInt("Сегментов на угол", ref t.CornerSegments, 1, 6);
            shapeChanged |= c2;

            // Вращение нижнего ряда песочницы — им проверяют, что свет остался мировым.
            // Ссылка кешируется: OnGUI зовётся по нескольку раз за кадр, поиск по сцене там не нужен.
            if (_sandbox == null)
                _sandbox = FindFirstObjectByType<WoodSandbox>();
            if (_sandbox == null)
                return;

            GUILayout.Space(6f);
            var spinning = Mathf.Abs(_sandbox.SpinSpeed) > 1e-3f;
            if (GUILayout.Toggle(spinning, " вращать нижний ряд") != spinning)
                _sandbox.SpinSpeed = spinning ? 0f : 18f;
        }

        private bool DrawGrain(WoodTuning t)
        {
            var c = false;
            Hint("Угол задаётся числом. Авторазворот «вдоль длинной стороны» — это и есть причина, " +
                 "по которой волокно раньше смотрело у каждой детали в свою сторону.");
            c |= Row("Линий на единицу", ref t.GrainScale, 1f, 12f);
            c |= Row("Сила", ref t.GrainStrength, 0f, 1f);
            c |= Row("Волнистость", ref t.GrainWarp, 0f, 1f);
            c |= Row("Резкость", ref t.GrainSharp, 1f, 12f);
            c |= Row("Разброс", ref t.GrainVary, 0f, 1f);
            c |= Row("Рельеф", ref t.GrainEmboss, 0f, 0.5f);
            c |= Row("Шероховатость", ref t.GrainNoise, 0f, 0.5f);
            c |= Row("Угол (град.)", ref t.GrainAngle, -180f, 180f);
            c |= Row("Вдоль длинной стороны", ref t.GrainAlignLong, 0f, 1f);
            c |= Row("Деталь → фигура", ref t.GrainSpace, 0f, 1f);

            // TASK-559: доски. Пока тумблер включён, ползунки выше деталям НЕ видны (кроме разворота и «деталь →
            // фигура») — каждая деталь берёт строку таблицы. Без этой подписи ползунки выглядели бы сломанными.
            GUILayout.Space(8f);
            var variety = GUILayout.Toggle(t.GrainVariety, " разные доски у деталей (варианты волокна)");
            if (variety != t.GrainVariety)
            {
                t.GrainVariety = variety;
                c = true;
            }

            if (!t.GrainVariety || t.GrainVariants == null || t.GrainVariants.Count == 0)
                return c;

            Hint("Доски включены: числа волокна выше деталям не видны — крутить строку доски ниже.");
            c |= RowInt("Досок на уровень", ref t.GrainVariantsPerLevel, 1,
                Mathf.Min(t.GrainVariants.Count, WoodTuning.MaxGrainVariants));
            c |= RowInt("Доска статики", ref t.StaticGrainVariant, 0, t.GrainVariants.Count - 1);
            c |= Row("Разброс фазы", ref t.GrainPhaseRange, 0f, 20f);

            GUILayout.BeginHorizontal();
            for (var i = 0; i < t.GrainVariants.Count; i++)
            {
                var on = _grainVariant == i;
                if (GUILayout.Toggle(on, $"V{i + 1}", GUI.skin.button) && !on)
                    _grainVariant = i;
            }
            GUILayout.EndHorizontal();

            _grainVariant = Mathf.Clamp(_grainVariant, 0, t.GrainVariants.Count - 1);
            var v = t.GrainVariants[_grainVariant];
            if (v == null)
                return c;
            GUILayout.Label(v.Name ?? $"V{_grainVariant + 1}", _small);
            c |= Row("Линий на единицу", ref v.GrainScale, 1f, 12f);
            c |= Row("Сила", ref v.GrainStrength, 0f, 1f);
            c |= Row("Волнистость", ref v.GrainWarp, 0f, 1f);
            c |= Row("Резкость", ref v.GrainSharp, 1f, 12f);
            c |= Row("Разброс", ref v.GrainVary, 0f, 1f);
            c |= Row("Рельеф", ref v.GrainEmboss, 0f, 0.5f);
            c |= Row("Шероховатость", ref v.GrainNoise, 0f, 0.5f);
            c |= Row("Угол, модуль (град.)", ref v.GrainAngle, 0f, 15f);
            return c;
        }

        /// <summary>Какую доску сейчас крутят ползунками (TASK-559).</summary>
        private int _grainVariant;

        private bool DrawLight(WoodTuning t)
        {
            var c = false;
            Hint("Все ширины — в мировых единицах (кубик ≈ 1), поэтому одинаковы на детали любого " +
                 "размера. Свет мировой: у повёрнутой детали светлая сторона остаётся сверху.");
            c |= Row("Угол света", ref t.LightAngle, 0f, 360f);
            GUILayout.Space(4f);
            c |= Row("Фаска: ширина", ref t.BevelWidth, 0f, 0.4f);
            c |= Row("Фаска: свет", ref t.BevelLight, 0f, 1f);
            c |= Row("Фаска: тень", ref t.BevelDark, 0f, 1f);
            GUILayout.Space(4f);
            c |= Row("Кант: ширина", ref t.RimWidth, 0f, 0.15f);
            c |= Row("Кант: свет", ref t.RimLight, 0f, 1f);
            c |= Row("Кант: тень", ref t.RimDark, 0f, 1f);
            GUILayout.Space(4f);
            c |= Row("Контур: ширина", ref t.LineWidth, 0f, 0.08f);
            c |= Row("Контур: сила", ref t.LineDark, 0f, 1f);
            GUILayout.Space(4f);
            c |= Row("Затемнение к краям", ref t.Ao, 0f, 0.6f);
            c |= Row("Его глубина", ref t.AoWidth, 0.02f, 1f);
            return c;
        }

        private bool DrawColors(WoodTuning t)
        {
            var c = false;
            c |= Row("Насыщенность", ref t.Saturation, 0.5f, 2f);
            c |= Row("Яркость", ref t.Brightness, 0.5f, 1.5f);

            GUILayout.Space(8f);

            // Главный тумблер этой вкладки: без него палитра красила только песочницу, а в игре
            // детали брали свои цвета из уровня — владелец настраивал одно, а видел другое.
            var toGame = GUILayout.Toggle(t.PaletteToGame, " применять палитру к деталям игры");
            if (toGame != t.PaletteToGame)
            {
                t.PaletteToGame = toGame;
                c = true;
            }
            Hint(t.PaletteToGame
                ? "Цвет каждой детали уровня заменён ближайшим по ТОНУ из палитры. 16 цветов уровней " +
                  "ложатся в эти 6, поэтому разные типы деталей могут совпасть по цвету — раскладка " +
                  "печатается в консоль строками «[wood] палитра»."
                : "Игра красит детали цветами из shapefill_levels.json, палитра ниже — только песочница.");

            // Ряд образцов: выбранный обведён. Больше шести в строку не влезает по ширине панели.
            GUILayout.BeginHorizontal();
            for (var i = 0; i < t.Palette.Count; i++)
            {
                if (i > 0 && i % 6 == 0)
                {
                    GUILayout.EndHorizontal();
                    GUILayout.BeginHorizontal();
                }
                var style = i == _selectedColor ? GUI.skin.box : GUI.skin.label;
                if (GUILayout.Button(Swatch(t.Palette[i]), style, GUILayout.Width(48f), GUILayout.Height(34f)))
                    _selectedColor = i;
            }
            GUILayout.EndHorizontal();

            _selectedColor = Mathf.Clamp(_selectedColor, 0, Mathf.Max(0, t.Palette.Count - 1));
            if (t.Palette.Count == 0)
                return c;

            var color = ParseColor(t.Palette[_selectedColor]);
            Color.RGBToHSV(color, out var hue, out var sat, out var val);
            var changedColor = false;
            changedColor |= Row("Тон", ref hue, 0f, 1f);
            changedColor |= Row("Насыщ.", ref sat, 0f, 1f);
            changedColor |= Row("Яркость", ref val, 0f, 1f);

            if (changedColor)
            {
                var hex = "#" + ColorUtility.ToHtmlStringRGB(Color.HSVToRGB(hue, sat, val));
                t.Palette[_selectedColor] = hex;
                _swatches.Remove(hex);
                c = true;
            }

            GUILayout.BeginHorizontal();
            GUILayout.Label(t.Palette[_selectedColor], _label, GUILayout.Width(120f));
            if (GUILayout.Button("копировать hex", GUILayout.Width(140f)))
            {
                GUIUtility.systemCopyBuffer = t.Palette[_selectedColor];
                Say("hex в буфере");
            }
            if (GUILayout.Button("+", GUILayout.Width(32f)))
            {
                t.Palette.Add(t.Palette[_selectedColor]);
                c = true;
            }
            if (GUILayout.Button("−", GUILayout.Width(32f)) && t.Palette.Count > 1)
            {
                t.Palette.RemoveAt(_selectedColor);
                c = true;
            }
            GUILayout.EndHorizontal();
            return c;
        }

        private bool DrawStates(WoodTuning t)
        {
            var c = false;
            Hint("Деталь ЖДЁТ запуска — приглушена; ПОЛЕТЕЛА — активная. В песочнице нижний ряд " +
                 "показывает пары: слева ждущая, справа активная. Третья пара — взведённая в паре.");

            GUILayout.Label("Ждёт (неактивная)", _label);
            c |= Row("Яркость", ref t.IdleBrightness, 0.2f, 1.2f);
            c |= Row("Цветность", ref t.IdleSaturation, 0f, 1.5f);

            GUILayout.Space(6f);
            GUILayout.Label("Полетела (активная)", _label);
            c |= Row("Яркость", ref t.ActiveBrightness, 0.5f, 1.5f);
            c |= Row("Цветность", ref t.ActiveSaturation, 0f, 1.5f);

            GUILayout.Space(6f);
            GUILayout.Label("Взведена в паре", _label);
            c |= Row("Увод в белый", ref t.ArmedWhite, 0f, 1f);

            // TASK-614: пунктир вокруг ждущей детали — гаснет на тапе. Длины в единицах мира.
            GUILayout.Space(6f);
            GUILayout.Label("Пунктир ждущей", _label);
            var dash = GUILayout.Toggle(t.IdleDash, " пунктир вокруг ещё не тапнутой детали");
            if (dash != t.IdleDash)
            {
                t.IdleDash = dash;
                c = true;
            }
            c |= Row("Толщина", ref t.IdleDashWidth, 0.01f, 0.15f);
            c |= Row("Штрих", ref t.IdleDashLength, 0f, 0.5f);
            c |= Row("Пробел", ref t.IdleDashGap, 0.02f, 0.5f);
            c |= Row("Отступ", ref t.IdleDashOffset, 0f, 0.3f);
            c |= Row("Скругление", ref t.IdleDashCorner, 0f, 0.4f);
            return c;
        }

        private bool DrawShadow(WoodTuning t)
        {
            var c = false;
            Hint("Смещение — мировое: у кувыркающейся детали тень не ездит вокруг неё.");
            c |= Row("Плотность", ref t.ShadowAlpha, 0f, 1f);
            c |= Row("Мягкость", ref t.ShadowSoft, 0.005f, 0.6f);
            c |= Row("Сдвиг X", ref t.ShadowOffsetX, -0.4f, 0.4f);
            c |= Row("Сдвиг Y", ref t.ShadowOffsetY, -0.4f, 0.4f);
            c |= Row("Размер", ref t.ShadowScale, 1f, 1.2f);

            GUILayout.Space(6f);
            var onWall = GUILayout.Toggle(t.ShadowOnlyOnWall, " тень только на стене, не на дереве");
            if (onWall != t.ShadowOnlyOnWall)
            {
                t.ShadowOnlyOnWall = onWall;
                c = true;
            }
            Hint(t.ShadowOnlyOnWall
                ? "Тень уведена по глубине за статику — пол сам её срезает. Между сложенными "
                  + "деталями тени тоже не будет: взамен уходит грязь на стыках."
                : "Тень ложится поверх всего дерева, включая пол под фигурой.");

            GUILayout.Space(8f);
            GUILayout.Label("Тень под рамкой превью", _label);
            Hint("Сдвиг и мягкость — в ДОЛЯХ стороны рамки: она вчетверо крупнее детали и ещё " +
                 "масштабируется под полосу экрана на каждом уровне.");
            c |= Row("Плотность", ref t.FrameShadowAlpha, 0f, 1f);
            c |= Row("Мягкость (доли)", ref t.FrameShadowSoft, 0.005f, 0.25f);
            c |= Row("Сдвиг X (доли)", ref t.FrameShadowOffsetX, -0.15f, 0.15f);
            c |= Row("Сдвиг Y (доли)", ref t.FrameShadowOffsetY, -0.15f, 0.15f);
            c |= Row("Размер", ref t.FrameShadowScale, 0.9f, 1.15f);
            return c;
        }

        /// <summary>
        /// Вкладка ВОЗДУХА: вентилятор, прозрачность плашки зоны и раскрытие потока.
        ///
        /// Зона пересобирается на каждое движение ползунка — <see cref="WoodTuning.Changed"/> ловит
        /// <c>AirZoneView</c>. Уровни с воздухом: mailbox, air_dunk, piggy_bank.
        /// </summary>
        private bool DrawAir(WoodTuning t)
        {
            var c = false;
            GUILayout.Label("Вентилятор", _label);
            Hint("Ширина упирается в потолок на всех трёх зонах: их грани шире арта. Выше 2.30 " +
                 "ползунок не пустит — столько кадр резервирует под корпус, чтобы его не срезало " +
                 "краем на узком телефоне.");
            c |= Row("Ширина: потолок", ref t.FanMaxWidth, 0.8f, 2.3f);
            c |= Row("Доля входной грани", ref t.FanFaceFill, 0.3f, 1.2f);
            c |= Row("Вылет за грань", ref t.FanOutset, 0f, 1f);
            c |= Row("Период лопастей", ref t.FanSpinPeriod, 0.03f, 0.3f);

            GUILayout.Space(8f);
            GUILayout.Label("Плашка зоны", _label);
            Hint("Сейчас выключена (ноль — меш не строится). Это была область силы: подняв ползунок, " +
                 "снова увидишь точные границы того, где воздух держит деталь.");
            c |= Row("Прозрачность", ref t.AirFillAlpha, 0f, 0.3f);

            GUILayout.Space(8f);
            GUILayout.Label("Конус потока", _label);
            Hint("Начало — в долях полуширины вентилятора, конец — в долях полуширины зоны. Больше " +
                 "единицы на конце значит «шире зоны», то есть накрыть с запасом.");
            c |= Row("Начало (у лопастей)", ref t.WindConeStart, 0.05f, 1.5f);
            c |= Row("Конец (на выходе)", ref t.WindConeEnd, 0.3f, 1.6f);
            c |= Row("Потолок раскрытия", ref t.WindConeMaxAngle, 0f, 60f);
            c |= Row("Плоскость (глубина)", ref t.WindConeFlat, 0f, 1f);
            c |= Row("Долёт: вертикаль", ref t.WindReach, 0.4f, 3f);
            c |= Row("Долёт: боковые", ref t.WindReachSide, 0.4f, 4f);
            c |= Row("Скорость: вертикаль", ref t.WindSpeedVertical, 3f, 30f);
            c |= Row("Скорость: боковые", ref t.WindSpeedSide, 3f, 30f);
            c |= Row("Густота", ref t.WindDensity, 0.2f, 3f);

            GUILayout.Space(8f);
            var donor = GUILayout.Toggle(t.DonorWind, " перенесённый эффект (иначе свой поток)");
            if (donor != t.DonorWind)
            {
                t.DonorWind = donor;
                c = true;
            }
            c |= Row("Поправка к размеру", ref t.DonorWindScale, 0.3f, 2f);
            return c;
        }

        // ------------------------------------------------------------------ мелочи

        private bool Row(string label, ref float value, float min, float max)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, _label, GUILayout.Width(150f));
            var v = GUILayout.HorizontalSlider(value, min, max, GUILayout.Width(140f));
            GUILayout.Label(v.ToString("0.###"), _small, GUILayout.Width(50f));
            GUILayout.EndHorizontal();

            if (Mathf.Abs(v - value) < 1e-6f)
                return false;
            value = v;
            return true;
        }

        private bool RowInt(string label, ref int value, int min, int max)
        {
            var f = value;
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, _label, GUILayout.Width(150f));
            var v = Mathf.RoundToInt(GUILayout.HorizontalSlider(f, min, max, GUILayout.Width(140f)));
            GUILayout.Label(v.ToString(), _small, GUILayout.Width(50f));
            GUILayout.EndHorizontal();

            if (v == value)
                return false;
            value = v;
            return true;
        }

        private void Hint(string text)
        {
            GUILayout.Label(text, _small);
            GUILayout.Space(4f);
        }

        private void Say(string text)
        {
            _status = text;
            _statusUntil = Time.realtimeSinceStartup + 3f;
        }

        private static Color ParseColor(string hex)
            => ColorUtility.TryParseHtmlString(hex, out var c) ? c : Color.magenta;

        /// <summary>Образец цвета: однопиксельная текстура на каждый hex, с кешем — иначе IMGUI
        /// плодил бы по текстуре на кадр.</summary>
        private Texture2D Swatch(string hex)
        {
            if (_swatches.TryGetValue(hex, out var tex) && tex != null)
                return tex;
            tex = new Texture2D(1, 1);
            tex.SetPixel(0, 0, ParseColor(hex));
            tex.Apply();
            _swatches[hex] = tex;
            return tex;
        }

        private void EnsureStyles()
        {
            if (_label != null)
                return;
            _label = new GUIStyle(GUI.skin.label) { fontSize = 13 };
            _title = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold };
            _small = new GUIStyle(GUI.skin.label) { fontSize = 11, wordWrap = true };
        }

        private void OnDestroy()
        {
            foreach (var tex in _swatches.Values)
                if (tex != null)
                    Destroy(tex);
            _swatches.Clear();
            if (_instance == this)
                _instance = null;
        }
    }
}
#endif
