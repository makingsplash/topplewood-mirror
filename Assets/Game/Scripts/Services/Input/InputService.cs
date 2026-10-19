using System.Collections.Generic;
using DataStorage;
using R3;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.Scripting;
using Game.Services.LogService;
using Game.Tools;
using Object = UnityEngine.Object;

namespace Game.Services.Input
{
    [Preserve]
    public sealed class InputService : IInputService
    {
        private readonly IInputBlockersHandler _blockersHandler;
        private readonly Controls _controls = new();
        
        private readonly ILogService _logService;
        
        private Vector2 _pointerStartPosition;
        private Vector2 _pointerCurrentPosition;
        
        private static int _uiRaycastLayer;
        
        private bool _denyFutureClick;
        private bool _isOverUi;

        private float _dragCumulative;
        private EventSystem _eventSystem;

        private bool _isEnabled;

        private readonly ReactiveProperty<bool> _pointer0Pressed = new();
        private float _pointer0StartTime;
        private Vector2 _pointer0StartPos;
        private Vector2 _pointer0Pos;
        private Vector2 _pointer0EndPos;

        private readonly ReactiveProperty<bool> _pointer1Pressed = new();

        private Vector2 _pointer1StartPos;
        private Vector2 _pointer1Pos;
        private Vector2 _pointer1EndPos;

        private Vector2 _prevMultiVec;


        private readonly Subject<Vector2> _pointer0HoldStarted = new();

        public Observable<Vector2> OnPointer0HoldStartedAsObservable() => _pointer0HoldStarted;
        
        private readonly Subject<Vector2> _pointer0HoldEnded = new();
        public Observable<Vector2> OnPointer0HoldEndedAsObservable() => _pointer0HoldEnded;


        private readonly ReactiveProperty<bool> _multitouchIsActive = new();
        
        public Observable<bool> IsMultiTouchIsActiveAsObservable() => _multitouchIsActive;


        private readonly Subject<Vector2> _onClicked = new();
        public Observable<Vector2> OnClickedAsObservable() => _onClicked;

        // TASK-114/TASK-374: the second finger's tap (co-release gesture). Emitted on finger-1 release under the SAME
        // blocker/UI/deny guards as pointer 0, so a real pinch (which sets _denyFutureClick via movement) still
        // suppresses it, while a stationary two-finger co-tap passes both fingers through.
        private readonly Subject<Vector2> _onPointer1Clicked = new();
        public Observable<Vector2> OnPointer1ClickedAsObservable() => _onPointer1Clicked;
        private bool _isOverUiP1;

        private bool _isGameplayClickBufferingEnabled;
        private bool _hasBufferedGameplayClick;
        private Vector2 _bufferedGameplayClickPosition;
        private float _bufferedGameplayClickTimestamp;
        
        
        private readonly Subject<Vector2> _onPointerDrag = new();
        public Observable<Vector2> OnPointerDragAsObservable() => _onPointerDrag;
        
        private readonly Subject<Vector2> _onMultiTouch = new(); 
        public Observable<Vector2> OnMultiTouchAsObservable() => _onMultiTouch;

        private readonly CompositeDisposable _compositeDisposable = new();

        // Метрики/состояния для двухпальцевого жеста
        private bool _twoFingerActive;
        private float _twoFingerLastDistance;

        public InputService(ILogService logService,
            IInputBlockersHandler blockersHandler)
        {
            _logService = logService;
            _blockersHandler = blockersHandler;
        }
        

        public void Enable()
        {
            if(_isEnabled) return;
            
            _eventSystem = Object.FindFirstObjectByType<EventSystem>();
            
            _uiRaycastLayer = LayerMask.NameToLayer("UI");

            _isOverUi = false;
            
            
            _controls.Game.Pointer_0_Position.started += OnPointer0PositionStarted;
            _controls.Game.Pointer_0_Position.performed += OnPointer0PositionPerformed;
            _controls.Game.Pointer_0_Position.canceled += OnPointer0PositionCanceled;
            
            _controls.Game.Pointer_0_Press.started += OnPointer0PressStarted;
            _controls.Game.Pointer_0_Press.performed += OnPointer0PressPerformed;
            _controls.Game.Pointer_0_Press.canceled += OnPointer0PressCanceled;
            
            
            _controls.Game.Pointer_1_Position.started += OnPointer1PositionStarted;
            _controls.Game.Pointer_1_Position.performed += OnPointer1PositionPerformed;
            _controls.Game.Pointer_1_Position.canceled += OnPointer1PositionCanceled;
            
            _controls.Game.Pointer_1_Press.started += OnPointer1PressStarted;
            _controls.Game.Pointer_1_Press.performed += OnPointer1PressPerformed;
            _controls.Game.Pointer_1_Press.canceled += OnPointer1PressCanceled;
            
            
            _controls.Game.Drag.started += OnPointerDragStarted;
            _controls.Game.Drag.performed += OnPointerDragPerformed;
            _controls.Game.Drag.canceled += OnPointerDragCanceled;
            
            
            _controls.Game.Scroll.started += OnScrollStarted;
            _controls.Game.Scroll.performed += OnScrollPerformed;
            _controls.Game.Scroll.canceled += OnScrollCanceled;
            
            
            //_pointer0Pressed.Subscribe(pressed => _logService.Log($"P 0 Pressed: {pressed}")).AddTo(_compositeDisposable);
            //_pointer1Pressed.Subscribe(pressed => _logService.Log($"P 1 Pressed: {pressed}")).AddTo(_compositeDisposable);
            
            
            // Реактивная подписка на мульти-тач: true, когда оба нажатия true
            _pointer0Pressed
                .CombineLatest(_pointer1Pressed, (p0, p1) => p0 && p1)
                .DistinctUntilChanged()
                .Subscribe(_multitouchIsActive, static (rp, v) => v.Value = rp)
                .AddTo(_compositeDisposable);
            
            // Инициализация/сброс метрик двух пальцев при входе/выходе из режима
            _multitouchIsActive
                .DistinctUntilChanged()
                .Subscribe(isActive =>
                {
                    //_logService.Log($"MultitouchIsActive: {isActive}");
                    
                    _twoFingerActive = isActive;
                    if (isActive)
                    {
                        _twoFingerLastDistance = Vector2.Distance(_pointer0Pos, _pointer1Pos);
                        var curMultiVec = (_pointer1Pos - _pointer0Pos).normalized;
                        _prevMultiVec = curMultiVec;
                    }
                    
                })
                .AddTo(_compositeDisposable);

            _controls.Enable();

            _isEnabled = true;
        }

        public bool TryConsumeBufferedGameplayClick(out Vector2 clickPosition)
        {
            if (!_hasBufferedGameplayClick)
            {
                clickPosition = default;
                return false;
            }

            var expirationSeconds = Data.Configs.InputConfig.GameplayClickBufferExpirationSeconds;
            if (expirationSeconds < 0f)
            {
                expirationSeconds = 0f;
            }

            if (Time.unscaledTime - _bufferedGameplayClickTimestamp > expirationSeconds)
            {
                ClearBufferedGameplayClick();
                clickPosition = default;
                return false;
            }

            clickPosition = _bufferedGameplayClickPosition;
            ClearBufferedGameplayClick();
            return true;
        }

        public void SetGameplayClickBufferingEnabled(bool isEnabled)
        {
            _isGameplayClickBufferingEnabled = isEnabled;
        }

        public void ClearBufferedGameplayClick()
        {
            _hasBufferedGameplayClick = false;
            _bufferedGameplayClickPosition = default;
            _bufferedGameplayClickTimestamp = 0f;
        }


        public void Disable()
        {
            if(!_isEnabled) return;
            
            _compositeDisposable.Clear();
            
            _controls.Game.Pointer_0_Position.started -= OnPointer0PositionStarted;
            _controls.Game.Pointer_0_Position.performed -= OnPointer0PositionPerformed;
            _controls.Game.Pointer_0_Position.canceled -= OnPointer0PositionCanceled;
            
            _controls.Game.Pointer_0_Press.started -= OnPointer0PressStarted;
            _controls.Game.Pointer_0_Press.performed -= OnPointer0PressPerformed;
            _controls.Game.Pointer_0_Press.canceled -= OnPointer0PressCanceled;
            
            
            _controls.Game.Pointer_1_Position.started -= OnPointer1PositionStarted;
            _controls.Game.Pointer_1_Position.performed -= OnPointer1PositionPerformed;
            _controls.Game.Pointer_1_Position.canceled -= OnPointer1PositionCanceled;
            
            _controls.Game.Pointer_1_Press.started -= OnPointer1PressStarted;
            _controls.Game.Pointer_1_Press.performed -= OnPointer1PressPerformed;
            _controls.Game.Pointer_1_Press.canceled -= OnPointer1PressCanceled;
            
            
            _controls.Game.Drag.started -= OnPointerDragStarted;
            _controls.Game.Drag.performed -= OnPointerDragPerformed;
            _controls.Game.Drag.canceled -= OnPointerDragCanceled;
            
            _controls.Game.Scroll.started -= OnScrollStarted;
            _controls.Game.Scroll.performed -= OnScrollPerformed;
            _controls.Game.Scroll.canceled -= OnScrollCanceled;

            _controls.Disable();
            _isEnabled = false;
            _isGameplayClickBufferingEnabled = false;
            ClearBufferedGameplayClick();
        }


        
        #region Point0Position
        private void OnPointer0PositionStarted(InputAction.CallbackContext context)
        {
            var pos = context.ReadValue<Vector2>();
           //_logService.Log($"Pointer0PositionStarted: {pos}");
            _pointer0StartPos = pos;
        }
        
        private void OnPointer0PositionPerformed(InputAction.CallbackContext context)
        {
            var pos = context.ReadValue<Vector2>();
           //_logService.Log($"Pointer0PositionPerformed: {pos}");
            _pointer0Pos = pos;

            if (_twoFingerActive)
            {
                EmitTwoFingerGesture();
            }
        }
        
        private void OnPointer0PositionCanceled(InputAction.CallbackContext context)
        {
            if (context.control is TouchControl touch)
            {
                // Пытаемся взять актуальное значение; если оно пустое после cancel — берем из предыдущего кадра
                _pointer0EndPos = touch.position.ReadValue();
                if (_pointer0EndPos == default)
                {
                    _pointer0EndPos = touch.position.ReadValueFromPreviousFrame();
                }
            }
            else
            {
                // Для мыши/пера продолжит работать обычное чтение
                _pointer0EndPos = context.ReadValue<Vector2>();
            }

           //_logService.Log($"Pointer0PositionCanceled: {_pointer0EndPos}");

        }
        #endregion Point0Position
        
        
        #region Point1Position
        private void OnPointer1PositionStarted(InputAction.CallbackContext context)
        {
            var pos = context.ReadValue<Vector2>();
           //_logService.Log($"Pointer1PositionStarted: {pos}");
            _pointer1StartPos = pos;
        }
        
        private void OnPointer1PositionPerformed(InputAction.CallbackContext context)
        {
            var pos = context.ReadValue<Vector2>();
           //_logService.Log($"Pointer1PositionPerformed: {pos}");
            _pointer1Pos = pos;

            if (_twoFingerActive)
            {
                EmitTwoFingerGesture();
            }
        }
        
        private void OnPointer1PositionCanceled(InputAction.CallbackContext context)
        {
            var pos = context.ReadValue<Vector2>();
           //_logService.Log($"Pointer1PositionCanceled: {pos}");
            _pointer1EndPos = pos;
        }
        #endregion Point1Position
        

        
        
        #region Pointer_0_Press
        private void OnPointer0PressStarted(InputAction.CallbackContext context)
        {
            _isOverUi = IsPointerOverUIObject(_pointer0Pos, _eventSystem);
           //_logService.Log($"OnPointer0PressStarted. IsOverUi: {_isOverUi}");

           _denyFutureClick = false; 
           
           _dragCumulative = 0f;
            
            if (!_isOverUi)
            {
                if (_blockersHandler.HasNotBlockers(InputActionTypeFlags.Pointer0Hold))
                {
                    _pointer0StartTime = Time.time;
                    _pointer0Pressed.Value = true;
                    _pointer0HoldStarted.OnNext(_pointer0Pos);
                }
            }
        }

        private void OnPointer0PressPerformed(InputAction.CallbackContext context)
        {
           //_logService.Log("OnPointer0PressPerformed: ");
            
            if(_isOverUi) return;
            
            if (_pointer0Pressed.Value)
            {
                
            }
        }
        
        
        private void OnPointer0PressCanceled(InputAction.CallbackContext context)
        {
            //_logService.Log($"OnPointer0PressCanceled. overUI: {_isOverUi}");
            
            //_logService.Log($"OnClicked: {_pointer0Pos}: _denyFutureClick: {_denyFutureClick}, cumulative: {_dragCumulative}");

            if (!_denyFutureClick)
            {
                var timeElapsed = Time.time - _pointer0StartTime;
                if (timeElapsed > Data.Configs.InputConfig.HoldTimeThreshold)
                {
                    _denyFutureClick = true;
                }
            }

            if (_blockersHandler.HasNotBlockers(InputActionTypeFlags.Click))
            {
                if (!_denyFutureClick && !_isOverUi)
                {
                    var clickPos = _controls.Game.Pointer_0_Position.ReadValue<Vector2>();

                    if (_isGameplayClickBufferingEnabled)
                    {
                        BufferGameplayClick(clickPos);
                    }

                    _onClicked.OnNext(clickPos);
                }
            }

            if (_blockersHandler.HasNotBlockers(InputActionTypeFlags.Pointer0Hold))
            {
                _pointer0HoldEnded.OnNext(_pointer0Pos);
            }

            _pointer0Pressed.Value = false;
            _isOverUi = false;
            _denyFutureClick = true;
            _dragCumulative = 0f;
        }
        
        #endregion Pointer_0_Press
        
        #region Pointer_1_Press
        private void OnPointer1PressStarted(InputAction.CallbackContext context)
        {
           //_logService.Log($"OnPointer1PressStarted");
            // TASK-114/TASK-374: a second finger DOWN no longer force-denies the tap. It used to unconditionally set
            // _denyFutureClick so any two-finger touch swallowed the gameplay tap (the pinch/zoom guard) — but that
            // also swallowed the co-release CO-TAP, the exact gesture the game now needs. A real pinch still denies via
            // MOVEMENT (EmitTwoFingerGesture / drag); a stationary co-tap passes. The only OnClicked consumer is the
            // gameplay adapter, and the pinch/twist stream has no subscriber, so this only affects gameplay taps.
            _isOverUiP1 = IsPointerOverUIObject(_pointer1Pos, _eventSystem);
            _pointer1Pressed.Value = true;
        }
        
        

        private void OnPointer1PressPerformed(InputAction.CallbackContext context)
        {
            //_logService.Log("OnPointer1PressPerformed");
            
            if (_pointer1Pressed.Value)
            {
                
            }
        }
        
        
        private void OnPointer1PressCanceled(InputAction.CallbackContext context)
        {
           //_logService.Log("OnPointer1PressCanceled");
            // TASK-114/TASK-374: emit the second finger's tap under the SAME guards as pointer 0 (OnPointer0PressCanceled):
            // not denied by a real pinch/drag, not over UI, and only when the Click action is not blocked. Position is
            // the last known finger-1 point (a tap barely moves it), which stays valid after the touch is lifted — the
            // live action read can return default once the finger is up.
            if (_blockersHandler.HasNotBlockers(InputActionTypeFlags.Click) && !_denyFutureClick && !_isOverUiP1)
                _onPointer1Clicked.OnNext(_pointer1Pos);

            _pointer1Pressed.Value = false;
            _isOverUiP1 = false;
        }
        
        #endregion Pointer_1_Press
        
        
        private void OnPointerDragStarted(InputAction.CallbackContext context)
        {
           //_logService.Log($"OnPointerDragStarted, overUI: {_isOverUi}");
           _dragCumulative = 0f;
        }
        
        private void OnPointerDragPerformed(InputAction.CallbackContext context)
        {
           if(!_pointer0Pressed.Value) return;
           
           //_logService.Log($"OnPointerDragPerformed _dragCumulative: {_dragCumulative}");
           
            if(_isOverUi) return;
            if (_multitouchIsActive.Value) return; // не мешаем двухпальцевому жесту
            
            
            var dragVec = context.ReadValue<Vector2>();
            var dragDistance = dragVec.magnitude;
            _dragCumulative += dragDistance;

            if (!_denyFutureClick)
            {
                _denyFutureClick = _dragCumulative > Data.Configs.InputConfig.DragDetectionDistance;
            }

            if (_blockersHandler.HasNotBlockers(InputActionTypeFlags.PointerDrag))
            {
                _onPointerDrag.OnNext(dragVec);
            }
        }
        
        
        private void OnPointerDragCanceled(InputAction.CallbackContext context)
        {
           //_logService.Log($"OnPointerDragCanceled. _dragCumulative: {_dragCumulative}");
        }
        
        private void OnScrollStarted(InputAction.CallbackContext context)
        {
            var scroll = context.ReadValue<Vector2>();
            //_logService.Log($"OnScrollStarted: {scroll}");
        }
        
        private void OnScrollPerformed(InputAction.CallbackContext context)
        {
            var scroll = context.ReadValue<Vector2>();

            var scaledScroll = new Vector2(scroll.x * Data.Configs.InputConfig.GetMouseHorizontalScrollMultiplier(),
                scroll.y);

            //_logService.Log($"OnScrollPerformed: {scroll} -> {scaledScroll}");

            if (_blockersHandler.HasNotBlockers(InputActionTypeFlags.MultiTouch))
            {
                _onMultiTouch.OnNext(scaledScroll);
            }
        }
        
        private void OnScrollCanceled(InputAction.CallbackContext context)
        {
            var scroll = context.ReadValue<Vector2>();
            //_logService.Log($"OnScrollCanceled: {scroll}");
        }
        
        
        
        private static readonly List<RaycastResult> _uiRaycastResults = new();
        
        private static bool IsPointerOverUIObject(Vector2 pos, EventSystem eventSystem)
        {
            if (eventSystem != null)
            {
                var eventData = new PointerEventData(eventSystem) {position = pos};
                _uiRaycastResults.Clear();
                eventSystem.RaycastAll(eventData, _uiRaycastResults);

                foreach (RaycastResult uiRaycastResult in _uiRaycastResults)
                {
                    if (IsUiHit(uiRaycastResult.module, uiRaycastResult.gameObject, _uiRaycastLayer))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// A raycast hit that owns the tap, so gameplay must not get it too: any uGUI graphic (its raycaster is a
        /// <see cref="UnityEngine.UI.GraphicRaycaster"/> — the graphic is raycast-enabled under a blocking canvas group, or
        /// the raycaster would not have returned it), or anything on the UI layer. TASK-638: the old rule was the layer
        /// alone, and every code-built HUD canvas (<c>new GameObject(...)</c> → layer Default: Hint, RESTART, settings
        /// gear, DEV) failed it — one tap on the Hint button reached the button AND the field. During the hint tour the
        /// field half was refused by the tour's gate, which answered with the miss pulse: the finger restarted on every
        /// correct tap.
        /// </summary>
        public static bool IsUiHit(BaseRaycaster module, GameObject hit, int uiLayer) =>
            module is UnityEngine.UI.GraphicRaycaster || (hit != null && hit.layer == uiLayer);

        private void EmitTwoFingerGesture()
        {
            // Текущее расстояние и угол
            var currDistance = Vector2.Distance(_pointer0Pos, _pointer1Pos);

            var curMultiVec = (_pointer1Pos - _pointer0Pos);
            if (curMultiVec.sqrMagnitude > 0.0001f)
                curMultiVec.Normalize();

            // Подписанный угол поворота: >0 — против часовой, <0 — по часовой
            var currAngle = Vector2.SignedAngle(_prevMultiVec, curMultiVec);

            var pinchDelta = currDistance - _twoFingerLastDistance;

            _twoFingerLastDistance = currDistance;
            _prevMultiVec = curMultiVec;

            // Фильтрация мелкого шума
            const float pinchEps = 0.01f;
            const float angleEps = 0.1f;

            var pinchOut = Mathf.Abs(pinchDelta) >= pinchEps ? pinchDelta : 0f;
            var twistOut = Mathf.Abs(currAngle) >= angleEps ? currAngle : 0f;

            _onMultiTouch.OnNext(new Vector2(twistOut, pinchOut));

            // TASK-114/TASK-374: deny the tap only when a REAL two-finger gesture (pinch/twist beyond the noise eps)
            // occurred — not merely because two fingers are down. A stationary co-tap (no movement past the eps) leaves
            // _denyFutureClick untouched so both fingers reach gameplay; a genuine pinch/twist still suppresses the tap.
            if (pinchOut != 0f || twistOut != 0f)
                _denyFutureClick = true;
        }

        private void BufferGameplayClick(Vector2 clickPos)
        {
            _bufferedGameplayClickPosition = clickPos;
            _bufferedGameplayClickTimestamp = Time.unscaledTime;
            _hasBufferedGameplayClick = true;
        }

    }
}
