using System;

namespace Common.Extensions
{
    public static class CleanCodeExtensions
    {
        public static T With<T>(this T self, Action<T> set)
        {
            set.Invoke(self);
            return self;
        }

        public static T With<T>(this T self, Action<T> set, bool when)
        {
            if (when)
                set.Invoke(self);

            return self;
        }

        public static T With<T>(this T self, Action<T> set, Func<T, bool> when)
        {
            if (when.Invoke(self))
                set.Invoke(self);

            return self;
        }

        public static T WithState<T, TState>(this T self, TState state, Action<T, TState> set)
        {
            set.Invoke(self, state);
            return self;
        }
        
        public static T WithState<T, TState1, TState2>(
            this T self, 
            TState1 state1, 
            TState2 state2, 
            Action<T, TState1, TState2> set)
        {
            set.Invoke(self, state1, state2);
            return self;
        }
        
        public static T WithState<T, TState>(
            this T self, 
            TState state, 
            Action<T, TState> set, 
            bool when)
        {
            if (when)
                set.Invoke(self, state);

            return self;
        }
        
        public static T WithState<T, TState1, TState2>(
            this T self, 
            TState1 state1, 
            TState2 state2, 
            Action<T, TState1, TState2> set, 
            bool when)
        {
            if (when)
                set.Invoke(self, state1, state2);

            return self;
        }
        
        public static T WithState<T, TState>(
            this T self,
            TState state, 
            Action<T, TState> set,
            Func<T, TState, bool> when)
        {
            if (when.Invoke(self, state))
                set.Invoke(self, state);

            return self;
        }
        
        public static T WithState<T, TState1, TState2>(
            this T self,
            TState1 state1,
            TState2 state2,
            Action<T, TState1, TState2> set,
            Func<T, TState1, TState2, bool> when)
        {
            if (when.Invoke(self, state1, state2))
                set.Invoke(self, state1, state2);

            return self;
        }
    }
}
