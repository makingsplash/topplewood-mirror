using R3;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Game
{
	public class ButtonWithPointerEvents : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
	{
		private readonly Subject<Unit> _onPointerDownSubject = new();
		private readonly Subject<Unit> _onPointerUpSubject = new();

		public Observable<Unit> OnPointerDownObservable() => _onPointerDownSubject;
		public Observable<Unit> OnPointerUpObservable() => _onPointerUpSubject;

		public void OnPointerDown(PointerEventData eventData)
		{
			_onPointerDownSubject.OnNext(Unit.Default);
		}

		public void OnPointerUp(PointerEventData eventData)
		{
			_onPointerUpSubject.OnNext(Unit.Default);
		}

		private void OnDestroy()
		{
			_onPointerDownSubject.Dispose();
			_onPointerUpSubject.Dispose();
		}
	}
}
