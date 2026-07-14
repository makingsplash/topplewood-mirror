using System;
using System.Linq;
using DG.Tweening;
using SGS.Infrastructure.Tools.Configs;
using SGS.Infrastructure.Tools.Rewards;
using SGS.Store.Addressables;
using SGS.Store.Ui;
using Sirenix.OdinInspector;
using UnityEngine;

namespace SGS.Store.Purchases
{
    [CreateAssetMenu(fileName = "StoreVisualConfig", menuName = "SGS/Store/StoreVisualConfig")]
    [GameConfig(ConfigName = "StoreVisualConfig", ConfigDir = "Configs", Group="store")]
    public class StoreVisualConfig : ScriptableObject
    {
        [Serializable]
        public abstract class PrefabLink
        {
            [LabelText("")]
            public string Id;
            [LabelText("")]
            [NonSerialized]
            public GameObject LoadedAsset;
        }
        
        [Serializable]
        public class PrefabLinkBlock : PrefabLink
        {
            [LabelText("")]
            [InlineProperty(LabelWidth = 35)]
            public AssetReferenceStoreBlockController PrefabReference;
        }
        
        [Serializable]
        public class PrefabLinkCard : PrefabLink
        {
            [LabelText("")]
            [InlineProperty(LabelWidth = 35)]
            public AssetReferenceStoreCardController PrefabReference;
        }

        [Serializable]
        public class SpriteLink
        {
            [LabelText("")]
            public string Id;
            [LabelText("")]
            [InlineProperty(LabelWidth = 35)]
            public Sprite[] SpriteReferences;
        }

        [Serializable]
        public class StoreAnimationSettings
        {
            public float CardClickDuration = 0.4f;
            public float CardClickScale = 1.1f;
            public float CardHideDuration = 0.5f;
            public float BlockShrinkDuration = 0.5f;
            public Ease BlockShrinkEasing = Ease.OutBack;
        }

        [Serializable]
        private class Data
        {
            public PrefabLinkBlock[] Blocks;
            public PrefabLinkCard[] Cards;
            public PrefabLinkCard[] StoreCards;
            public SpriteLink[] Icons;
            public SpriteLink[] PackIcons;
            public StoreAnimationSettings AnimationSettings;
        }

        [SerializeField]
        private Data _preset;

        public StoreAnimationSettings AnimationSettings => _preset.AnimationSettings;
        public bool HasBlockPrefab(string blockId)
        {
            return _preset.Blocks.Any(l => l.Id.Equals(blockId, StringComparison.InvariantCultureIgnoreCase));
        }

        public bool HasCardPrefab(string visualId)
        {
            return _preset.Cards.Any(l => l.Id.Equals(visualId, StringComparison.InvariantCultureIgnoreCase));
        }

        public StoreBlockController GetBlockPrefab(string blockId)
        {
            var blockRef =
                _preset.Blocks.FirstOrDefault(l => l.Id.Equals(blockId, StringComparison.InvariantCultureIgnoreCase));
            if (blockRef == null)
            {
                Debug.LogError($"No block {blockId} in store visual config. Returning default");
                blockRef = _preset.Blocks[0];
            }

            if (blockRef.LoadedAsset is null)
            {
                blockRef.LoadedAsset = blockRef.PrefabReference.LoadAssetAsync<GameObject>().WaitForCompletion();
            }

            return Instantiate(blockRef.LoadedAsset).GetComponent<StoreBlockController>();
        }

        public StoreCardController GetCardPrefab(string visualId)
        {
            var cardRef = _preset.Cards.FirstOrDefault(l => l.Id.Equals(visualId, StringComparison.InvariantCultureIgnoreCase));
            if (cardRef == null)
            {
                Debug.LogError($"No card {visualId} in store visual config. Returning default");
                cardRef = _preset.Cards[0];
            }

            if (cardRef.LoadedAsset is null)
            {
                cardRef.LoadedAsset = cardRef.PrefabReference.LoadAssetAsync<GameObject>().WaitForCompletion();
            }

            return Instantiate(cardRef.LoadedAsset).GetComponent<StoreCardController>();
        }

        public StoreCardController GetStoreCardPrefab(string visualId)
        {
            var cardRef = _preset.StoreCards.FirstOrDefault(l =>
                l.Id.Equals(visualId, StringComparison.InvariantCultureIgnoreCase));
            if (cardRef == null)
            {
                Debug.LogError($"No card {visualId} in store visual config. Returning default");
                cardRef = _preset.Cards[0];
            }

            if (cardRef.LoadedAsset is null)
            {
                cardRef.LoadedAsset = cardRef.PrefabReference.LoadAssetAsync<GameObject>().WaitForCompletion();
            }

            return Instantiate(cardRef.LoadedAsset).GetComponent<StoreCardController>();
        }

        public Sprite GetIcon(string iconType, int stage = 0)
        {
            var spriteRef =
                _preset.Icons.FirstOrDefault(l => l.Id.Equals(iconType, StringComparison.InvariantCultureIgnoreCase));
            if (spriteRef == null)
            {
                Debug.LogError($"No icon {iconType} in store visual config. Returning default");
                spriteRef = _preset.Icons[0];
            }

            var idx = Mathf.Clamp(stage, 0, spriteRef.SpriteReferences.Length);
            return spriteRef.SpriteReferences[idx];
        }

        public Sprite GetPackIcon(string iconType, int stage = 0)
        {
            var spriteRef =
                _preset.PackIcons.FirstOrDefault(l => l.Id.Equals(iconType, StringComparison.InvariantCultureIgnoreCase));
            if (spriteRef == null)  
            {
                Debug.LogError($"No pack icon {iconType} in store visual config. Returning default");
                spriteRef = _preset.PackIcons[0];
            }

            var idx = Mathf.Clamp(stage, 0, spriteRef.SpriteReferences.Length);
            return spriteRef.SpriteReferences[idx];
        }
        
        public virtual string GetRewardText(ProductRewardData reward)
        {
            return reward.Amount.ToString();
        }

        public virtual Sprite GetRewardIcon(ProductRewardData reward)
        {
            return GetPackIcon(reward.RewardType,0);
        }

        public virtual Sprite GetSingleRewardIcon(ProductRewardData reward, string productId)
        {
            return GetIcon(reward.RewardType,0);
        }
    }
}