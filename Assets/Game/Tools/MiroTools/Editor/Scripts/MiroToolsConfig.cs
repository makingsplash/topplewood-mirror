using UnityEngine;
using Sirenix.OdinInspector;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Cysharp.Threading.Tasks;

namespace MiroTools
{
    [CreateAssetMenu(fileName = "MiroToolsConfig", menuName = "Configs/MiroTools Config")]
    public class MiroToolsConfig : ScriptableObject
    {
        public System.Action OnCreateBoardRequest;

        [BoxGroup("API Configuration")]
        public bool DebugLogEnabled = true;
    
        [BoxGroup("API Configuration")]
        [PasswordPropertyText]
        public string ApiToken;

        [BoxGroup("Workspace Configuration")]
        [ValueDropdown("GetWorkspaceOptions")]
        [LabelText("Target Workspace (for new boards)")]
        public string SelectedWorkspaceId;

        [BoxGroup("Workspace Configuration")]
        [Sirenix.OdinInspector.ReadOnly]
        public List<ApiClient.WorkspaceData> AvailableWorkspaces = new();

        [BoxGroup("Board Selection")]
        [HorizontalGroup("BoardSelection")]
        [ValueDropdown("GetBoardOptions")]
        [LabelText("Active Board")]
        public string BoardId;

        [BoxGroup("Board Selection")]
        [HorizontalGroup("BoardSelection", Width = 140)]
        [Button("Create New Board")]
        public void CreateNewBoardPrompt()
        {
            OnCreateBoardRequest?.Invoke();
        }

        [BoxGroup("Board Selection")]
        [Sirenix.OdinInspector.ReadOnly]
        public List<ApiClient.BoardData> AvailableBoards = new();

        public async UniTask RefreshBoards()
        {
            if (string.IsNullOrEmpty(ApiToken)) return;
            
            var boards = await ApiClient.GetBoards(ApiToken);
            AvailableBoards = boards;

            var workspaces = await ApiClient.GetWorkspaces(ApiToken);
            AvailableWorkspaces = workspaces;
        }

        private IEnumerable<ValueDropdownItem> GetBoardOptions()
        {
            if (AvailableBoards == null || AvailableBoards.Count == 0)
                return new[] { new ValueDropdownItem("None", "") };

            return AvailableBoards.Select(b => new ValueDropdownItem(b.Name ?? b.Id, b.Id));
        }

        private IEnumerable<ValueDropdownItem> GetWorkspaceOptions()
        {
            if (AvailableWorkspaces == null || AvailableWorkspaces.Count == 0)
            {
                return new[] { new ValueDropdownItem("None (Use Default)", "") };
            }

            return AvailableWorkspaces.Select(w => new ValueDropdownItem(w.Name ?? w.Id, w.Id));
        }
    }
}
