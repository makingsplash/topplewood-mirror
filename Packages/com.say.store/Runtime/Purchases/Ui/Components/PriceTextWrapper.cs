using System;
using System.Threading.Tasks;
using TMPro;

namespace SGS.Store.Purchases.Ui.Components
{
    public class PriceTextWrapper
    {
        public event Action<PriceTextWrapper> Finished;
        
        private readonly TMP_Text _text;
        private readonly PurchaseService _service;
        private readonly string _purchaseId;

        private int _currentPointsCount;
        private const int _updateDelayMs = 800;
        private const int _maxPointsCount = 3;

        private bool _isFinished;

        public PriceTextWrapper(PurchaseService service, TMP_Text text, string purchaseId)
        {
            _text = text;
            _service = service;
            _purchaseId = purchaseId;
            if (_text == null)
            {
                return;
            }

            _currentPointsCount = _maxPointsCount;
            UpdateLoop();
        }

        public void DeInit()
        {
            if (!_isFinished)
            {
                _isFinished = true;
                Finished?.Invoke(this);
            }
        }

        private async void UpdateLoop()
        {
            do
            {
                string price = _service.GetLocalisedPrice(_purchaseId);
                if (_text == null)
                {
                    DeInit();
                    return;
                }
                
                if (string.IsNullOrEmpty(price))
                {
                    _text.text = new string('.', _currentPointsCount);
                    _currentPointsCount = _currentPointsCount % _maxPointsCount + 1;
                }
                else
                {
                    _text.text = price;
                    DeInit();
                    return;
                }

                await Task.Delay(_updateDelayMs);

            } while (!_isFinished);
        }
    }
}