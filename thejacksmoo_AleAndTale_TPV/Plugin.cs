using BepInEx;
using UnityEngine;
using System.Reflection;

/*
 * Note for [using HarmonyLib;] : Added this so I can fix the issue of a new tool added  
 *      to the hotbar (like a newly purchased tool or a tool picked up from a chest) 
 *      showing the FPV tool.
 */
using HarmonyLib;


/*
 * single line notes = //
 */
namespace jakjakmooAleAndTaleThirdPerson
{
    [BepInPlugin(
        "com.jakjakmoo.aleandtale.thirdperson",
        "JakJakMoo Ale & Tale Third Person",
        "0.1.0"
    )]
    public class Plugin : BaseUnityPlugin
    {
        /*
         * [private] indicates that only the code in this plugin can access the [variable] 
         *      so that other [classes] won't just go and change it. We didn't go over this in
         *      the C++ class but it's similar in C++:
         *      
         *      C++ example:
         *      
         *      private:
         *          float itemVisibilityTimer;
         */


        /* 
         * Note for [private bool thirdPersonEnabled = false;]
         * 
         * [private] = only code in this plugin will modify this code
         * [bool] = only returns true or false
         */
        private bool thirdPersonEnabled = false;
        private bool savedOriginalPositions = false;
        private Vector3 originalFPViewPosition;
        private Vector3 originalCameraPosition;
        private FieldInfo interactiveHitDistField;
        //Part of hotbar slot 1 handheld not showing on load
        private MethodInfo onSelectedHandItemDataIdMethod;
        // Adding attack freeze frame for block animation since there is no existing block animation
        private FieldInfo playerAnimTPAnimatorField;
        private static readonly int axeChopAnimHash =
            Animator.StringToHash("Axe Chop");
        private FieldInfo invNumsGoField;
        // Part of furniture placement fix
        private Vector3 temporaryFPViewPosition;
        private Vector3 temporaryCameraPosition;
        //A float is a [data type] that allows decimals rather than just a whole integer.
        private float originalInteractiveHitDist;
        // Part of the fix for FPV tool showing when adding to hotbar
        private Harmony harmony;
        // Part of the fix for FPV tool showing when adding to hotbar
        private static Plugin Instance;

        private void Awake()
        {

            // Part of the fix for FPV tool showing when adding to hotbar
            Instance = this;

            Logger.LogInfo("Ale & Tale Third Person mod loaded!");
            
            // Part of fix for interaction distance being too far when TPV camera enabled
            interactiveHitDistField = typeof(PlayerInventory).GetField(
                "interactiveHitDist",
                 BindingFlags.Instance | BindingFlags.NonPublic
            );

            // Part of adding a block animation
            playerAnimTPAnimatorField = typeof(PlayerAnimTP).GetField(
                "animator",
                BindingFlags.Instance | BindingFlags.NonPublic
            );

            // Part of adding a block animation
            invNumsGoField = typeof(PlayerInventory).GetField(
                "_invNumsGo",
                BindingFlags.Instance | BindingFlags.NonPublic
            );

            onSelectedHandItemDataIdMethod = typeof(PlayerAnimTP).GetMethod(
                "OnSelectedHandItemDataId",
                BindingFlags.Instance | BindingFlags.NonPublic,
                null,
                new[] { typeof(uint), typeof(uint) },
                null
            );

            harmony = new Harmony("com.jakjakmoo.aleandtale.thirdperson");

            harmony.Patch(
                AccessTools.Method(
                    typeof(PlayerInventory),
                    "OnInventoryAdd",
                    new[] { typeof(ushort) }
                ),
                postfix: new HarmonyMethod(
                    typeof(Plugin),
                    nameof(OnInventoryAddPostfix)
                )
            );

            harmony.Patch(
                AccessTools.Method(
                    typeof(PlayerInventory),
                    "OnInventoryChange",
                    new[] { typeof(ushort) }
                    ),
                postfix: new HarmonyMethod(
                    typeof(Plugin),
                    nameof(OnInventoryAddPostfix)
                )
            );

            // Part of furniture placement fix
            harmony.Patch(
                AccessTools.Method(
                    typeof(SurfaceSnapTool),
                    "FixedUpdate"
                ),
                prefix: new HarmonyMethod(
                    typeof(Plugin),
                    nameof(SurfaceSnapToolPrefix)
                ),
                postfix: new HarmonyMethod(
                    typeof(Plugin),
                    nameof(SurfaceSnapToolPostfix)
                )
            );

            // Part of furniture placement fix pt.2
            harmony.Patch(
                AccessTools.Method(
                    typeof(SurfaceSnapFurnitureTool),
                    "FixedUpdate"
                ),
                prefix: new HarmonyMethod(
                    typeof(Plugin),
                    nameof(SurfaceSnapToolPrefix)
                ),
                postfix: new HarmonyMethod(
                    typeof(Plugin),
                    nameof(SurfaceSnapToolPostfix)
                )
            );

            // Part of furniture placement fix pt.2
            harmony.Patch(
                AccessTools.Method(
                    typeof(PlaceSnapFurnitureTool),
                    "FixedUpdate"
                ),
                prefix: new HarmonyMethod(
                    typeof(Plugin),
                    nameof(SurfaceSnapToolPrefix)
                ),
                postfix: new HarmonyMethod(
                    typeof(Plugin),
                    nameof(SurfaceSnapToolPostfix)
                )
            );

        }

        private void Update()
        {

            // Blocking 65% freeze frame
            if (thirdPersonEnabled &&
                PlayerInput.Instance != null &&
                PlayerInventory.Instance != null &&
                PlayerNet.Instance != null &&
                PlayerNet.Instance.playerAnimTP != null)
            {
                GameObject[] hotbarObjects =
                    (GameObject[])invNumsGoField.GetValue(
                        PlayerInventory.Instance
                    );

                byte selectedSlot =
                    PlayerInventory.Instance.selectedSlot;

                if (hotbarObjects != null &&
                    selectedSlot < hotbarObjects.Length &&
                    hotbarObjects[selectedSlot] != null &&
                    hotbarObjects[selectedSlot].TryGetComponent<WeaponTool>(
                        out WeaponTool currentWeapon) &&
                    currentWeapon.canBlock &&
                    PlayerInput.Instance.GetAimInputHeld())
                {
                    Animator tpAnimator =
                        (Animator)playerAnimTPAnimatorField.GetValue(
                            PlayerNet.Instance.playerAnimTP
                        );

                    tpAnimator.Play(
                        axeChopAnimHash,
                        1,
                        0.65f
                    );
                }
            }

            // Part of enabling TPV with F5
            if (!Input.GetKeyDown(KeyCode.F5))
                return;

            if (PlayerMovement.Instance == null ||
                PlayerMovement.Instance.mainCamera == null ||
                PlayerNet.Instance == null ||
                PlayerNet.Instance.playerAnimTP == null ||
                PlayerNet.Instance.playerAnimTP.playerAvatar == null)
            {
                Logger.LogWarning("Player objects not found yet.");
                return;
            }

            Transform mainCamera = PlayerMovement.Instance.mainCamera.transform;
            Transform fpView = mainCamera.parent;
            Transform tpAvatar =
                PlayerNet.Instance.playerAnimTP.playerAvatar.transform;

            if (!savedOriginalPositions)
            {
                originalFPViewPosition = fpView.localPosition;
                originalCameraPosition = mainCamera.localPosition;
                originalInteractiveHitDist =
                    (float)interactiveHitDistField.GetValue(PlayerInventory.Instance);
                savedOriginalPositions = true;
            }

            thirdPersonEnabled = !thirdPersonEnabled;

            if (thirdPersonEnabled)
            {
                // Show the full third-person character.
                PlayerNet.Instance.playerAnimTP.playerAvatar.SetVisible(true, true);

                uint selectedItemId =
                    PlayerNet.Instance.selectedHandItemDataId.Value;

                onSelectedHandItemDataIdMethod?.Invoke(
                    PlayerNet.Instance.playerAnimTP,
                    new object[] { 1u, selectedItemId }
                );

                interactiveHitDistField.SetValue(PlayerInventory.Instance, 5f);

                // Hide FPV handheld item meshes.
                SetHeldItemRenderers(fpView, "_fp(Clone)", false);

                // Show TPV handheld item meshes.
                SetHeldItemRenderers(tpAvatar, "_tp(Clone)", true);

                /*
                 * Raise the camera up to y = 1.8.
                 * 
                 * Note to self: When using RuntimeUnityEditor, the change in Y was not 
                 *  allowed under Main Camera and had to be done "1 folder up" in FPV.
                 */
                Vector3 fpPosition = fpView.localPosition;
                fpPosition.y = 1.8f;
                fpView.localPosition = fpPosition;

                // Move the camera backward to z = -2.
                Vector3 cameraPosition = mainCamera.localPosition;
                cameraPosition.z = -2f;
                mainCamera.localPosition = cameraPosition;

                Logger.LogInfo("Third Person ON");
            }
            else
            {
                // Hide the full TPV character avatar (mesh?).
                PlayerNet.Instance.playerAnimTP.playerAvatar.SetVisible(false, true);

                interactiveHitDistField.SetValue(
                    PlayerInventory.Instance,
                    originalInteractiveHitDist
                );

                // Restore FPV handeheld item meshes.
                SetHeldItemRenderers(fpView, "_fp(Clone)", true);

                // Hide TPV handheld item meshes.
                SetHeldItemRenderers(tpAvatar, "_tp(Clone)", false);

                // Restore original FPV Y position.
                fpView.localPosition = originalFPViewPosition;

                // Restore the original FPV Z camera position.
                mainCamera.localPosition = originalCameraPosition;

                Logger.LogInfo("Third Person OFF");
            }
        }

        // Part of furniture placement fix
        private static void SurfaceSnapToolPrefix()
        {

            if (Instance == null || !Instance.thirdPersonEnabled)
                return;

            Transform mainCamera = PlayerMovement.Instance.mainCamera.transform;
            Transform fpView = mainCamera.parent;

            Instance.temporaryFPViewPosition = fpView.localPosition;
            Instance.temporaryCameraPosition = mainCamera.localPosition;

            fpView.localPosition = Instance.originalFPViewPosition;
            mainCamera.localPosition = Instance.originalCameraPosition;
        }

        // Part of furniture placement fix
        private static void SurfaceSnapToolPostfix()
        {
            if (Instance == null || !Instance.thirdPersonEnabled)
                return;

            Transform mainCamera = PlayerMovement.Instance.mainCamera.transform;
            Transform fpView = mainCamera.parent;

            fpView.localPosition = Instance.temporaryFPViewPosition;
            mainCamera.localPosition = Instance.temporaryCameraPosition;
        }

        /*
         * Part of the fix for FPV tool showing when adding to hotbar.
         * 
         * onInventoryChange and onInventoryAdd both "recreate" the FPV tools when 
         * adding a new tool to the hotbar or moving a tool from the player inventory
         * to the hotbar. To fix this, we need to re-hide the FPV tool after these 
         * actions have taken place.
         */
        private static void OnInventoryAddPostfix()
        {
            // added after original fix failed
            if (Instance != null)
                Instance.Logger.LogInfo("OnInventoryAddPostfix fired!");

            // part of original fix
            if (Instance == null || !Instance.thirdPersonEnabled)
                return;

            if (PlayerMovement.Instance == null ||
                PlayerMovement.Instance.mainCamera == null)
                return;

            Transform fpView = PlayerMovement.Instance.mainCamera.transform.parent;

            Instance.SetHeldItemRenderers(
                fpView,
                "_fp(Clone)",
                false
            );

        }

        private void SetHeldItemRenderers(
            Transform root,
            string itemSuffix,
            bool visible)
        {
            Transform[] objects = root.GetComponentsInChildren<Transform>(true);

            foreach (Transform obj in objects)
            {
                if (!obj.name.EndsWith(itemSuffix))
                    continue;

                Renderer[] renderers =
                    obj.GetComponentsInChildren<Renderer>(true);

                foreach (Renderer renderer in renderers)
                {
                    renderer.enabled = visible;
                }
            }
        }
    }
}