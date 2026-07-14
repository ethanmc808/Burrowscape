using UnityEngine;
using UnityEngine.U2D.Animation;

public class ForceSpriteSkinAlwaysUpdate : MonoBehaviour
{
    [ContextMenu("Enable Always Update On All Sprite Skins")]
    private void EnableAlwaysUpdate()
    {
        SpriteSkin[] allSkins = GetComponentsInChildren<SpriteSkin>(true);
        foreach (SpriteSkin skin in allSkins)
        {
            skin.alwaysUpdate = true;
        }
        Debug.Log($"Enabled Always Update on {allSkins.Length} Sprite Skins.");
    }
}