namespace SGS.Services.Analytics
{
    public class TutorialAnalytics
    {
        public void LogTutorialStep(string tutorialName, string stepName)
        {
            if (string.IsNullOrEmpty(tutorialName) || string.IsNullOrEmpty(stepName))
            {
                return;
            }
            GlobalService.SayKit.TrackTutorialStep(tutorialName, stepName);
        }

        public void LogTutorialComplete()
        {
            GlobalService.SayKit.TrackTutorialCompleted();
        }
    }
}