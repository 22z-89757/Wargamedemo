using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;
using Wargame.Units;

namespace Wargame.Battle
{
    [DisallowMultipleComponent, RequireComponent(typeof(Unit))]
    public sealed class UnitBattleCameraSet : MonoBehaviour
    {
        [SerializeField] private CinemachineCamera[] focusCameras = new CinemachineCamera[4];
        [SerializeField] private CinemachineCamera shotCamera;

        public CinemachineCamera ShotCamera => shotCamera;

        public void Configure(CinemachineCamera[] focus, CinemachineCamera shot)
        { focusCameras = focus; shotCamera = shot; }

        public CinemachineCamera GetFocusCamera(int direction) =>
            focusCameras != null && focusCameras.Length == 4 ? focusCameras[(direction % 4 + 4) % 4] : null;

        public IEnumerable<CinemachineCamera> Cameras
        {
            get
            {
                if (focusCameras != null)
                    foreach (CinemachineCamera camera in focusCameras)
                        if (camera != null) yield return camera;
                if (shotCamera != null) yield return shotCamera;
            }
        }

        public bool ValidateConfiguration(Faction faction, out string error)
        {
            error = null;
            if (faction == Faction.Enemy)
            {
                if (focusCameras == null || focusCameras.Length != 4)
                { error = "敌人需要四个棋盘朝向聚焦相机。"; return false; }
                var unique = new HashSet<CinemachineCamera>();
                for (int i = 0; i < focusCameras.Length; i++)
                {
                    CinemachineCamera camera = focusCameras[i];
                    if (camera == null || !camera.enabled || !camera.gameObject.activeSelf || camera.Follow != transform ||
                        camera.LookAt != transform || camera.GetComponent<CinemachineFollow>() == null ||
                        camera.GetComponent<CinemachineRotationComposer>() == null || !unique.Add(camera))
                    { error = $"敌人缺少有效或唯一的第 {i} 个朝向聚焦相机。"; return false; }
                }
            }
            else if (faction == Faction.Player)
            {
                if (shotCamera == null || !shotCamera.enabled || !shotCamera.gameObject.activeSelf || shotCamera.Follow != transform ||
                    shotCamera.GetComponent<CinemachineFollow>() == null ||
                    shotCamera.GetComponent<CinemachineRotationComposer>() == null)
                { error = "玩家需要跟随 Unit 视觉根节点的射击相机。"; return false; }
            }
            return true;
        }

        public void SetShotTarget(Transform target)
        {
            if (shotCamera != null) shotCamera.LookAt = target;
        }
    }
}
