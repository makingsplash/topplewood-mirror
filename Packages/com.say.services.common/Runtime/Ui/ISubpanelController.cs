namespace SGS.Services.Ui
{
    public interface ISubpanelController
    {
        void Init();
        void DeInit();

        void SetVisible(bool value);
    }
}