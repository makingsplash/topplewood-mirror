namespace SGS.Services.Date_Time
{
    public class GameTimerService : TimerService
    {
        protected override string TimerName => "GameTimer";
        
        public override void OnUpdate()
        {
            if (Service.IsReady && !Service.Pause.IsPaused)
            {
                base.OnUpdate();
            }
        }
        
        public override void OnLateUpdate()
        {
            if (Service.IsReady && !Service.Pause.IsPaused)
            {
                base.OnLateUpdate();
            }
        }
    }
}