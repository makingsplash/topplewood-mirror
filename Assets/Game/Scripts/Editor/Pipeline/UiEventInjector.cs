using UnityEngine;
using UnityEngine.EventSystems;

namespace Game.Editor.Pipeline
{
    /// <summary>
    /// GENERIC (проекто-независимый) инжектор uGUI-указательных событий для приёмочных команд Unity Pipeline (TASK-028). Гонит НАСТОЯЩИЙ
    /// путь клика через <see cref="ExecuteEvents"/> — ровно как палец игрока: попадает в
    /// <see cref="IPointerClickHandler"/> цели (Button → onClick → View-семы игры), поэтому все гарды/листенеры,
    /// висящие на этих семах, срабатывают идентично живому тапу (в отличие от модельных шорткатов, которые семы минуют).
    ///
    /// Не знает ни одного имени конкретной игры — принимает голый <see cref="GameObject"/>/экранную точку.
    /// Слой studio-template (generic-команды пакета pipeline: ui_click). Проектные команды игры (например
    /// `<игра>_tap_ui`) живут отдельно и лишь резолвят цель + зовут этот инжектор.
    /// </summary>
    public static class UiEventInjector
    {
        /// <summary>
        /// Инъекция pointer-click по КОНКРЕТНОМУ объекту (иерархия: если сам объект не обрабатывает клик,
        /// событие всплывает к родителю — как это делает EventSystem). Respect'ит <c>Button.interactable</c> и
        /// активность: неинтерактивная/выключенная кнопка «проглотит» клик no-op'ом — так же, как под пальцем.
        /// Возвращает <c>true</c>, если какой-то <see cref="IPointerClickHandler"/> получил событие (цель жива).
        /// </summary>
        public static bool ClickObject(GameObject target)
        {
            if (target == null) return false;
            var data = new PointerEventData(EventSystem.current)
            {
                button = PointerEventData.InputButton.Left,
                clickCount = 1,
            };
            var handled = ExecuteEvents.ExecuteHierarchy(target, data, ExecuteEvents.pointerClickHandler);
            return handled != null;
        }
    }
}
