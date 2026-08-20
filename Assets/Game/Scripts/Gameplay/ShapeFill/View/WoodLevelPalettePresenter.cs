using System;
using Gameplay.Core;
using R3;
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
        }

        public void Dispose()
        {
            _subscription?.Dispose();
            _subscription = null;
        }
    }
}
