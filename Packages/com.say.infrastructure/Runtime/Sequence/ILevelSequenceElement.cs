using System;
using System.Collections.Generic;

namespace SGS.Infrastructure.Sequence
{
    public interface ILevelSequenceElement
    {
        event Action Finished;

        void Start(Dictionary<string, object> context);
    }
}