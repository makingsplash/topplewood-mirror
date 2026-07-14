using System;
using DynamicDebugMenu.Elements;
using SGS.Data;
using SGS.Services.DebugLog;

namespace SGS.Services.DebugTools.Submenu
{
    public static class LogFilterSubmenu
    {
        public static void Create(DDMContext context)
        {
            DDMSubmenu.Create(context, "Console Filter", () => context.Main.CreatePage("Console Filter", GenerateContent));
        }

        private static void GenerateContent(DDMContext context)
        {
#if GPIE_DEVELOPMENT            
            foreach (var option in DebugLogType.GetConstantValues())
            {
                DDMCheckbox.Create(context, option.Key,
                    () => (DebugLogType.FromIntMask(SayData.Debug.DebugLogType.Value) & option.Value) == option.Value,
                    v => UpdateLogOption(option.Value, v));
            }
#endif            
        }

        private static void UpdateLogOption(DebugLogType optionValue, bool set)
        {
            var value = DebugLogType.FromIntMask(SayData.Debug.DebugLogType.Value);

            var newValue = set ? value | optionValue : value & ~optionValue;
            SayData.Debug.DebugLogType.Value = newValue.Mask;
            GlobalService.DebugLog.SetMask(newValue);
        }


    }
}