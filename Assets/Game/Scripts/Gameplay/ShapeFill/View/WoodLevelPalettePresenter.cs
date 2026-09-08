using System;
using System.Collections.Generic;
using System.Text;
using Gameplay.Core;
using R3;
using UnityEngine;
using VContainer.Unity;

namespace Gameplay.View
{
    /// <summary>
    /// Сообщает вью-слою, какой уровень сейчас на экране: ставит <see cref="WoodTuning.CurrentLevelId"/>
    /// на загрузку уровня. По нему <see cref="PieceView"/> находит цветовую схему Иттена этого уровня.
    ///
    /// Отдельная точка входа, а не строчка в чужом презентере: схема цвета — не дело кадрирования
    /// камеры и не дело превью, а <see cref="PieceView"/> знать про кампанию не должен вовсе.
    /// Зеркалит <see cref="CameraFramingPresenter"/>: подписка на <c>LevelLoaded</c> плюс разовый
    /// вызов в <see cref="Start"/> — уровень может быть загружен ДО старта точек входа
    /// (GameControllerRunner стреляет событием синхронно).
    /// </summary>
    public sealed class WoodLevelPalettePresenter : IStartable, IDisposable
    {
        private readonly GameModel _model;
        private IDisposable _subscription;

        public WoodLevelPalettePresenter(GameModel model) => _model = model;

        public void Start()
        {
            _subscription = _model.LevelLoaded.Subscribe(_ => Apply());
            Apply();
        }

        private void Apply()
        {
            var id = _model.CurrentLevel?.LevelId;
            if (WoodTuning.CurrentLevelId == id)
                return;

            WoodTuning.CurrentLevelId = id;

            // Детали уже могли родиться со старой схемой — перекрасить их можно только событием.
            WoodTuning.Current.Apply(rebuildMeshes: false);

            // TASK-332: снять МОЛЧАНИЕ вокруг авторского цвета. Схема Иттена (§D24) молча подменяет авторский цвет
            // детали/статики — автор видит "#EFD9A1" в манифесте, валидаторе и диффе, но на экране рисуется другое
            // (на этом сгорели два «контроля» в TASK-354). Ничего НЕ перекрашиваем — только логируем перекрытые
            // цвета в точке загрузки уровня. Единственный источник условий — сам WoodTuning (OverridesStaticColor /
            // OverridesPieceColor зеркалят резолверы), поэтому лог не может разойтись с тем, что реально рисуется.
            LogOverriddenAuthoredColors(_model.CurrentLevel);
        }

        private static void LogOverriddenAuthoredColors(LevelData level)
        {
            if (level == null)
                return;

            var tuning = WoodTuning.Current;
            var dead = new List<string>();

            if (tuning.OverridesStaticColor && level.StaticVisuals != null)
                for (var i = 0; i < level.StaticVisuals.Count; i++)
                {
                    var authored = level.StaticVisuals[i].Color;
                    if (!string.IsNullOrEmpty(authored))
                        dead.Add($"static[{i}] {authored}→env");
                }

            if (level.Pieces != null)
            {
                var seenKinds = new HashSet<string>();
                foreach (var placement in level.Pieces)
                {
                    var kind = placement.Type?.Kind;
                    var authored = placement.Type?.Color;
                    if (string.IsNullOrEmpty(kind) || string.IsNullOrEmpty(authored) || !seenKinds.Add(kind))
                        continue;
                    if (tuning.OverridesPieceColor(level.LevelId, kind, out var hex))
                        dead.Add($"'{kind}' {authored}→{hex}");
                }
            }

            if (dead.Count == 0)
                return;

            var sb = new StringBuilder();
            sb.Append("[wood] INFO: у уровня '").Append(level.LevelId)
              .Append("' авторский цвет перекрыт схемой Иттена (рисуется НЕ то, что в данных): ");
            for (var i = 0; i < dead.Count; i++)
            {
                if (i > 0)
                    sb.Append(", ");
                sb.Append(dead[i]);
            }
            Debug.Log(sb.ToString());
        }

        public void Dispose()
        {
            _subscription?.Dispose();
            _subscription = null;
        }
    }
}
