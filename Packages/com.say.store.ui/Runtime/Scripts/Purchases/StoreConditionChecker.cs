using System;
using System.Collections.Generic;
using System.Text;
using SGS.Services;
using SGS.Services.Containers;

namespace SGS.Store.Purchases
{
    public class StoreConditionChecker : ConditionCheckerBase
    {
        private readonly Dictionary<string, Func<IConditionChecker.Context, bool>> _freeFunctions = new()
        {
            { "premium", c => GlobalService.SayKit.IsPremium()},
            { "purchased", c => ItemPurchased(c.ProductId)},
        };

        private readonly Dictionary<string, Func<IConditionChecker.Context, object[], bool>> _parametricFunctions = new()
        {
            { "purchased", (c, p) => ItemPurchased(p[0] as string)},
            { "level", (c, p) => LevelPassed(p[0] as int? ?? -1)},
        };

        private static bool ItemPurchased(string productId)
        {
            if( productId == null)
            {
                return false;
            }

            return GlobalServiceContainer.Get<GamePurchaseService>().PersistentStorage.PurchaseHistory.ContainsKey(productId);
        }
        
        private static bool LevelPassed(int level)
        {
            if (level < 0)
            {
                return false;
            }
            return GlobalServiceContainer.Get<GamePurchaseService>().CurrentLevel() >= level;
        }

        private StringBuilder _errorSink = null;
        private string _errorPrefix;

        public void VerifyCondition(string condition, StringBuilder sb, string errorPrefix)
        {
            _errorSink = sb;
            _errorPrefix = errorPrefix;

            var f = ParseCondition(condition);

            _errorSink = null;
            _errorPrefix = null;
        }

        protected override Func<IConditionChecker.Context, bool> CreateCondition(string name, object[] parameters)
        {
            var searchName = name.ToLowerInvariant(); 
            if (parameters.Length == 0)
            {
                if (_freeFunctions.TryGetValue(searchName, out var condition))
                {
                    return condition;
                }
            }
            else
            {
                if (_parametricFunctions.TryGetValue(searchName, out var condition))
                {
                    return c => condition(c, parameters);
                }
            }
            ReportError("", $"Unknown condition {name} in purchases config");
            return DummyCondition;
        }

        protected override void ReportError(string context, string error)
        {
            if (_errorSink == null)
            {
                GlobalService.DebugLog.LogError($" Error parsing condition at pouiimt {context}: {error}");
            }
            else
            {
                _errorSink.Append($"{_errorPrefix} error in condition at point {context}: {error}");
            }
        }
    }
}