using SGS.Infrastructure.Sequence;
using SGS.Services.Containers;
using UnityEngine;

namespace SGS.Infrastructure.Core
{
    public abstract class LevelController : MonoBehaviour
    {
        protected LocalServiceContainer _service;
        
        private void Start()
        {
            InitData();
            InitServices();

            InitGameSequence sequence = GetInitGameSequence();
            sequence.GameplayReady += OnGameplayReady;
            sequence.Start();
        }

        public void BeforeDestroy()
        {
            DeInitData();
            DeInitService();
        }

        private void OnGameplayReady(IGameplayController gameplayController)
        {
            gameplayController.Win += OnWin;
            gameplayController.Revive += OnRevive;
            gameplayController.DebugRestart += OnDebugRestart;
            gameplayController.Lose += OnLose;

            gameplayController.Start();
        }

        protected virtual void InitServices()
        {
            _service = LocalServiceContainer.Create();
            RegisterServices();
        }

        protected virtual void InitData()
        {
        }

        protected virtual void DeInitService()
        {
            _service.DeInit();
        }

        protected abstract void DeInitData();
        
        private void OnWin()
        {
            GameSequence sequence = GetWinSequence();
            sequence.AddToContext(GameSequenceTags.LevelController, this);
            sequence.Start();
        }

        private void OnLose(LoseReason reason)
        {
            GameSequence sequence = reason == LoseReason.BackToHome
                ? GetBackToHomeSequence()
                : GetLoseSequence();
            sequence.AddToContext(GameSequenceTags.LevelController, this);
            sequence.AddToContext(GameSequenceTags.LoseReason, reason);
            sequence.Start();
        }

        private void OnRevive()
        {
            var sequence = GetReviveSequence();
            sequence.AddToContext(GameSequenceTags.LoseReason, LoseReason.Revive);
            sequence.Start();
        }
        
        private void OnDebugRestart()
        {
            var sequence = GetDebugSequence();
            sequence.AddToContext(GameSequenceTags.LevelController, this);
            sequence.AddToContext(GameSequenceTags.LoseReason, LoseReason.Debug);
            sequence.Start();
        }

        protected abstract void RegisterServices();
        protected abstract InitGameSequence GetInitGameSequence();
        protected abstract GameSequence GetWinSequence();
        protected abstract GameSequence GetLoseSequence();
        protected virtual GameSequence GetBackToHomeSequence() => GetLoseSequence();
        protected abstract GameSequence GetReviveSequence();
        protected abstract GameSequence GetDebugSequence();
        
    }
}
