using UnityEngine;
namespace SashimiBoy
{
    public enum DayWorldInteractionKind { Bed, Shop }
    public sealed class DayWorldInteractable : MonoBehaviour, IInteractable, IInteractionAvailability
    {
        public DayWorldInteractionKind kind;
        public DayWorldSceneDirector director;
        public bool IsAvailable => DayWorldFlow.Active && (kind == DayWorldInteractionKind.Shop
            ? DayWorldRules.ShopAvailable(SaveManager.Instance.Current) : DayWorldRules.BedAvailable(SaveManager.Instance.Current));
        public string Prompt => kind==DayWorldInteractionKind.Shop ? "사장님과 장비 거래하기" : "잠들기";
        public void Interact(GameObject actor)
        {
            if(director == null || !IsAvailable) return;
            if(kind==DayWorldInteractionKind.Bed) director.UseBed(actor); else director.OpenShop();
        }
    }

}
