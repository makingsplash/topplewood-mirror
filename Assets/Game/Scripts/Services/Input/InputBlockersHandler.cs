using System;
using System.Collections.Generic;

namespace Game.Services.Input
{
    public class BlockersToken
    {
        private readonly int _tokenId;
        private readonly InputBlockersHandler _blockersHandler;
        private bool _isDisposed;

        public BlockersToken(int tokenId, InputBlockersHandler blockersHandler)
        {
            _tokenId = tokenId;
            _blockersHandler = blockersHandler;
        }

        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            _blockersHandler?.RemoveBlockers(_tokenId);
        }
    }

    public interface IInputBlockersHandler
    {
        BlockersToken AddBlockers(InputActionTypeFlags flags);
        BlockersToken AddBlockAllExcept(InputActionTypeFlags allowedFlags);
        bool HasBlockers(InputActionTypeFlags actionTypeFlags);
        bool HasNotBlockers(InputActionTypeFlags actionTypeFlags);
        void ClearAllBlockers();
    }

    public class InputBlockersHandler : IInputBlockersHandler, IDisposable
    {
        private readonly Dictionary<int, InputActionTypeFlags> _inputBlockers = new();
        private int _nextBlockerId = 1;

        public BlockersToken AddBlockers(InputActionTypeFlags flags)
        {
            if (flags == InputActionTypeFlags.None)
            {
                return new BlockersToken(0, this);
            }

            int id = _nextBlockerId++;
            _inputBlockers[id] = flags;
            return new BlockersToken(id, this);
        }
        
        public BlockersToken AddBlockAllExcept(InputActionTypeFlags allowedFlags)
        {
            if (allowedFlags == InputActionTypeFlags.None)
                return AddBlockers(InputActionTypeFlags.All);

            InputActionTypeFlags flagsToBlock = InputActionTypeFlags.All & ~allowedFlags;
            return AddBlockers(flagsToBlock);
        }

        public void RemoveBlockers(int blockersId)
        {
            if (blockersId == 0)
                return;

            _inputBlockers.Remove(blockersId);
        }

        public bool HasBlockers(InputActionTypeFlags actionTypeFlags)
        {
            if (actionTypeFlags == InputActionTypeFlags.None)
                return false;

            InputActionTypeFlags combinedFlags = GetCombinedFlags();
            return (combinedFlags & actionTypeFlags) != 0;
        }

        public bool HasNotBlockers(InputActionTypeFlags actionTypeFlags)
        {
            return !HasBlockers(actionTypeFlags);
        }

        public void ClearAllBlockers()
        {
            _inputBlockers.Clear();
        }

        private InputActionTypeFlags GetCombinedFlags()
        {
            InputActionTypeFlags result = InputActionTypeFlags.None;

            foreach (var flags in _inputBlockers.Values)
            {
                result |= flags;
            }

            return result;
        }

        public void Dispose()
        {
            _inputBlockers.Clear();
        }
    }
}
