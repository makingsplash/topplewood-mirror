using UnityEngine;

namespace SGS.Store.Ui
{
    public class GridStoreBlockController: StoreBlockController
    {
        [SerializeField]
        protected int ColumnsCount;
        [SerializeField]
        protected Vector2 GridStart = new(0, 0);
        [SerializeField]
        protected Vector2 GridSize = new(100, 100);
        [SerializeField]
        protected float BlockSpacingAfter;

        public override void LayoutCards()
        {
            Vector2 nextPosition = GridStart;
            int row = 0;
            int col = 0;
            foreach (var card in Cards)
            {
                if (!card.Enabled)
                {
                    continue;
                }
                col += card.Width;
                if (col > ColumnsCount)
                {
                    nextPosition.x = GridStart.x;
                    nextPosition.y += GridSize.y;
                    col = card.Width;
                    row += 1;
                }
                card.SetPosition(nextPosition);
                nextPosition.x += GridSize.x * card.Width;
            }
            
            
            var rectTransform = (RectTransform)transform;

            Vector2 sizeDelta = rectTransform.sizeDelta;
            if (Cards.Count == 0)
            {
                sizeDelta.x = 0;
                sizeDelta.y = 0;
            }
            else
            {
                sizeDelta.x = row == 0 ? (nextPosition.x) : GridStart.x + GridSize.x * ColumnsCount;
                sizeDelta.y = nextPosition.y + GridSize.y + BlockSpacingAfter;
                
            }

            CardsContainer.sizeDelta = sizeDelta;

            rectTransform.sizeDelta = sizeDelta;
        }
    }
}