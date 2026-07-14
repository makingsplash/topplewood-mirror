using System;

namespace SGS.Store.Purchases.Data
{
    public struct StoreCardStateData
    {
        public StoreCardsState State;
        public TimeSpan Timer;
        public int AdsLeft;
    }
}