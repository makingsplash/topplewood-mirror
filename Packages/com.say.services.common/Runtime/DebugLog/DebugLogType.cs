using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace SGS.Services.DebugLog
{
    
    public readonly partial struct DebugLogType 
    {
        private readonly int _value;
        public static readonly DebugLogType None = new (0);
        public static readonly DebugLogType ApplicationFlow = new(1 << 0);
        public static readonly DebugLogType Analytics = new(1 << 1);
        public static readonly DebugLogType Input = new(1 << 2);
        public static readonly DebugLogType Profiling = new(1 << 3);
        public static readonly DebugLogType Time = new(1 << 4);
        public static readonly DebugLogType Items = new(1 << 5);

        public static readonly DebugLogType Default = new(ApplicationFlow | Analytics | Time);
        public static readonly DebugLogType All = new(~None);

        public int Mask => _value;

        private DebugLogType(int value)
        {
            _value = value;
        }
        private DebugLogType(DebugLogType value)
        {
            _value = value._value;
        }


        public static DebugLogType FromIntMask(int mask)
        {
            return new(mask);
        }

#if GPIE_DEVELOPMENT
        private static List<KeyValuePair<string, DebugLogType>> _allValues;
        public static List<KeyValuePair<string, DebugLogType>> GetConstantValues()
        {
            int CountBits(int v)
            {
                uint value = (uint)v;
                int count = 0;
                while (value != 0)
                {
                    count++;
                    value &= value - 1;
                }

                return count;
            }

            if (_allValues == null)
            {
                _allValues = new();
                var type = typeof(DebugLogType);
                foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Static))
                {
                    if (field.FieldType != typeof(DebugLogType))
                    {
                        continue;
                    }

                    var fieldValue = (DebugLogType)field.GetValue(null);
                    if (fieldValue.Mask == 0)
                    {
                        continue;
                    }
                    _allValues.Add(new KeyValuePair<string, DebugLogType>(field.Name, fieldValue));
                }

                // Preset values first, then individual selectors
                _allValues.Sort((x, y) =>
                {
                    var d = CountBits(y.Value.Mask) - CountBits(x.Value.Mask);
                    return d != 0 ? d : x.Value.Mask - y.Value.Mask;
                });
            }

            return _allValues;
        }
#endif
        
        public override int GetHashCode()
        {
            return _value;
        }
        
        public override bool Equals(object obj)
        {
            return obj is DebugLogType other && _value == other._value;
        }

        public static bool operator ==(DebugLogType a, DebugLogType b)
        {
            return a._value == b._value;
        }

        public static bool operator !=(DebugLogType a, DebugLogType b)
        {
            return !(a == b);
        }

        public static DebugLogType operator |(DebugLogType a, DebugLogType b)
        {
            return new(a._value | b._value);
        }
        
        public static DebugLogType operator &(DebugLogType a, DebugLogType b)
        {
            return new(a._value & b._value);
        }
        
        public static DebugLogType operator ~(DebugLogType a)
        {
            return new(~a._value);
        }
    }
}
