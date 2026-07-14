using SGS.Services.Interfaces;

namespace SGS.Services.Date_Time
{
    public class DateService : IService
    {
        public void Init()  { }
        public void DeInit() { }

        public System.DateTime GetUtcNow()
        {
#if GPIE_DEVELOPMENT
            return System.DateTime.UtcNow;// + TimeSpan.FromDays(1) + TimeSpan.FromHours(27);
#else
            return System.DateTime.UtcNow;
#endif
        }
    }
}