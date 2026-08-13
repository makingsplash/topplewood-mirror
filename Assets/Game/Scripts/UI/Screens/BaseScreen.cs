using System;
using Services;
using SGS.Services.Ui;
using UnityEngine;
using UnityEngine.Scripting.APIUpdating;

namespace Ui.Screens
{
    [MovedFrom(sourceNamespace: "UI.Screens")]
    public abstract class BaseScreen : MonoBehaviour, IScreenController
    {
        [SerializeField] private string _screenName;
        
        public event Action OnClose;
        public GameObject GameObject => gameObject;

        private string ScreenName
        {
            get
            {
                if (string.IsNullOrEmpty(_screenName))
                {
                    return gameObject.name;
                }

                return _screenName;
            }
        }

        public virtual void Init()
        {
            
            
            GlobalService.SayKit.TrackScreen(ScreenName);
        }

        public virtual void DeInit()
        {
            OnClose?.Invoke();
            OnClose = null;
        }

        public void Close()
        {
            // TASK-126: маскирующий `?.` убран. Геттер Service.UI теперь гарантирует не-null после бута
            // или бросает — молчаливый no-op ("Close() ничего не делает"), стоивший нам TASK-120, устранён.
            Service.UI.Hide(this);
        }
    }
}
