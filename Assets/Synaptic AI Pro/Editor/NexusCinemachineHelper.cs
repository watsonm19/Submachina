using System;
using System.Collections.Generic;
using UnityEngine;
using SynapticAIPro;
using UnityEditor;

#if CINEMACHINE_2
using Cinemachine;
#elif CINEMACHINE_3
using Unity.Cinemachine;
using UnityEngine.Splines;
#endif

namespace SynapticPro
{
    /// <summary>
    /// Cinemachine operation helper
    /// Provides Cinemachine features such as Virtual Camera, FreeLook, Brain, etc.
    /// </summary>
    public static class NexusCinemachineHelper
    {
        /// <summary>
        /// Create Virtual Camera
        /// </summary>
        public static string CreateVirtualCamera(Dictionary<string, string> parameters)
        {
#if CINEMACHINE_2
            try
            {
                string name = parameters.ContainsKey("name") ? parameters["name"] : "CM vcam1";

                // Create Virtual Camera object
                GameObject vcamObj = new GameObject(name);
                var vcam = vcamObj.AddComponent<CinemachineVirtualCamera>();

                // Set priority
                if (parameters.ContainsKey("priority"))
                {
                    vcam.Priority = int.Parse(parameters["priority"]);
                }

                // Set position
                if (parameters.ContainsKey("position"))
                {
                    var pos = ParseVector3(parameters["position"]);
                    vcamObj.transform.position = pos;
                }

                // Set Follow/LookAt
                if (parameters.ContainsKey("follow"))
                {
                    var followTarget = GameObject.Find(parameters["follow"]);
                    if (followTarget != null)
                    {
                        vcam.Follow = followTarget.transform;
                    }
                }

                if (parameters.ContainsKey("lookAt"))
                {
                    var lookAtTarget = GameObject.Find(parameters["lookAt"]);
                    if (lookAtTarget != null)
                    {
                        vcam.LookAt = lookAtTarget.transform;
                    }
                }

                // Set Lens
                if (parameters.ContainsKey("fov"))
                {
                    vcam.m_Lens.FieldOfView = float.Parse(parameters["fov"]);
                }
                if (parameters.ContainsKey("nearClipPlane"))
                {
                    vcam.m_Lens.NearClipPlane = float.Parse(parameters["nearClipPlane"]);
                }
                if (parameters.ContainsKey("farClipPlane"))
                {
                    vcam.m_Lens.FarClipPlane = float.Parse(parameters["farClipPlane"]);
                }
                if (parameters.ContainsKey("dutch"))
                {
                    vcam.m_Lens.Dutch = float.Parse(parameters["dutch"]);
                }
                if (parameters.ContainsKey("orthographic"))
                {
                    vcam.m_Lens.Orthographic = bool.Parse(parameters["orthographic"]);
                }
                if (parameters.ContainsKey("orthographicSize"))
                {
                    vcam.m_Lens.OrthographicSize = float.Parse(parameters["orthographicSize"]);
                }

                // Add Body (Follow) component
                if (vcam.Follow != null || parameters.ContainsKey("bodyType"))
                {
                    string bodyType = parameters.ContainsKey("bodyType") ? parameters["bodyType"] : "Transposer";
                    AddBodyComponent(vcam, bodyType, parameters);
                }

                // Add Aim (LookAt) component
                if (vcam.LookAt != null || parameters.ContainsKey("aimType"))
                {
                    string aimType = parameters.ContainsKey("aimType") ? parameters["aimType"] : "Composer";
                    AddAimComponent(vcam, aimType, parameters);
                }

                // Add Noise (camera shake)
                if (parameters.ContainsKey("noiseProfile") || parameters.ContainsKey("noiseAmplitude") || parameters.ContainsKey("noiseFrequency"))
                {
                    AddNoiseComponent(vcam, parameters);
                }

                Undo.RegisterCreatedObjectUndo(vcamObj, "Create Virtual Camera");
                Selection.activeGameObject = vcamObj;

                return $"Virtual Camera '{name}' created successfully with priority {vcam.Priority}";
            }
            catch (Exception e)
            {
                return $"Error: Failed to create Virtual Camera - {e.Message}";
            }
#elif CINEMACHINE_3
            try
            {
                string name = parameters.ContainsKey("name") ? parameters["name"] : "CM vcam1";

                // Create Cinemachine Camera object (Cinemachine 3.x)
                GameObject vcamObj = new GameObject(name);
                var vcam = vcamObj.AddComponent<CinemachineCamera>();

                // Set priority
                if (parameters.ContainsKey("priority"))
                {
                    vcam.Priority.Value = int.Parse(parameters["priority"]);
                }

                // Set position
                if (parameters.ContainsKey("position"))
                {
                    var pos = ParseVector3(parameters["position"]);
                    vcamObj.transform.position = pos;
                }

                // Set Follow/LookAt (Tracking in CM3)
                if (parameters.ContainsKey("follow"))
                {
                    var followTarget = GameObject.Find(parameters["follow"]);
                    if (followTarget != null)
                    {
                        vcam.Target.TrackingTarget = followTarget.transform;
                    }
                }

                if (parameters.ContainsKey("lookAt"))
                {
                    var lookAtTarget = GameObject.Find(parameters["lookAt"]);
                    if (lookAtTarget != null)
                    {
                        vcam.Target.LookAtTarget = lookAtTarget.transform;
                    }
                }

                // Set Lens (no m_ prefix in CM3)
                if (parameters.ContainsKey("fov"))
                {
                    vcam.Lens.FieldOfView = float.Parse(parameters["fov"]);
                }
                if (parameters.ContainsKey("nearClipPlane"))
                {
                    vcam.Lens.NearClipPlane = float.Parse(parameters["nearClipPlane"]);
                }
                if (parameters.ContainsKey("farClipPlane"))
                {
                    vcam.Lens.FarClipPlane = float.Parse(parameters["farClipPlane"]);
                }
                if (parameters.ContainsKey("dutch"))
                {
                    vcam.Lens.Dutch = float.Parse(parameters["dutch"]);
                }
                if (parameters.ContainsKey("orthographic"))
                {
                    bool isOrthographic = bool.Parse(parameters["orthographic"]);
                    vcam.Lens.ModeOverride = isOrthographic
                        ? LensSettings.OverrideModes.Orthographic
                        : LensSettings.OverrideModes.None;
                }
                if (parameters.ContainsKey("orthographicSize"))
                {
                    vcam.Lens.OrthographicSize = float.Parse(parameters["orthographicSize"]);
                }

                // Add Body (Follow) component - CM3 uses different architecture
                if (vcam.Target.TrackingTarget != null || parameters.ContainsKey("bodyType"))
                {
                    string bodyType = parameters.ContainsKey("bodyType") ? parameters["bodyType"] : "Follow";
                    AddBodyComponent3(vcam, bodyType, parameters);
                }

                // Add Aim (LookAt) component
                if (vcam.Target.LookAtTarget != null || parameters.ContainsKey("aimType"))
                {
                    string aimType = parameters.ContainsKey("aimType") ? parameters["aimType"] : "Composer";
                    AddAimComponent3(vcam, aimType, parameters);
                }

                // Add Noise (camera shake)
                if (parameters.ContainsKey("noiseProfile") || parameters.ContainsKey("noiseAmplitude") || parameters.ContainsKey("noiseFrequency"))
                {
                    AddNoiseComponent3(vcam, parameters);
                }

                Undo.RegisterCreatedObjectUndo(vcamObj, "Create Virtual Camera");
                Selection.activeGameObject = vcamObj;

                return $"Virtual Camera '{name}' created successfully with priority {vcam.Priority.Value}";
            }
            catch (Exception e)
            {
                return $"Error: Failed to create Virtual Camera - {e.Message}";
            }
#else
            return "Error: Cinemachine package not installed. Please install via Package Manager.";
#endif
        }

        /// <summary>
        /// Create FreeLook Camera
        /// </summary>
        public static string CreateFreeLookCamera(Dictionary<string, string> parameters)
        {
#if CINEMACHINE_2
            try
            {
                string name = parameters.ContainsKey("name") ? parameters["name"] : "CM FreeLook1";

                GameObject freeLookObj = new GameObject(name);
                var freeLook = freeLookObj.AddComponent<CinemachineFreeLook>();

                // Set Follow (REQUIRED)
                if (!parameters.ContainsKey("follow"))
                {
                    // Clean up the camera we just created
                    GameObject.DestroyImmediate(freeLookObj);

                    return $"Error: FreeLook camera creation failed\n\n" +
                           $"Reason: 'follow' parameter is required\n\n" +
                           $"FreeLook cameras must track a target GameObject.\n\n" +
                           $"Example usage:\n" +
                           $"  unity_create_freelook_camera(name='PlayerCamera', follow='Player')\n" +
                           $"  unity_create_freelook_camera(follow='Cube', priority=10)\n\n" +
                           $"Tip: Create the target GameObject first, then create the FreeLook camera";
                }

                var followTarget = GameObject.Find(parameters["follow"]);
                if (followTarget == null)
                {
                    // Clean up the camera we just created
                    GameObject.DestroyImmediate(freeLookObj);

                    // Provide helpful error with solutions
                    return $"Error: FreeLook camera creation failed\n\n" +
                           $"Reason: Follow target '{parameters["follow"]}' not found in scene\n\n" +
                           $"Solutions:\n" +
                           $"  1. Create a GameObject named '{parameters["follow"]}' first\n" +
                           $"  2. Check spelling - GameObject names are case-sensitive\n" +
                           $"  3. Check Hierarchy window for existing GameObjects\n\n" +
                           $"Example:\n" +
                           $"  unity_create_gameobject(name='Player', position='0,0,0')\n" +
                           $"  unity_create_freelook_camera(name='PlayerCamera', follow='Player')";
                }

                freeLook.Follow = followTarget.transform;
                freeLook.LookAt = followTarget.transform; // Default is Follow target

                // Set LookAt (optional)
                if (parameters.ContainsKey("lookAt"))
                {
                    var lookAtTarget = GameObject.Find(parameters["lookAt"]);
                    if (lookAtTarget != null)
                    {
                        freeLook.LookAt = lookAtTarget.transform;
                    }
                }

                // Configure three rigs
                if (parameters.ContainsKey("topRigHeight"))
                    freeLook.m_Orbits[0].m_Height = float.Parse(parameters["topRigHeight"]);
                if (parameters.ContainsKey("topRigRadius"))
                    freeLook.m_Orbits[0].m_Radius = float.Parse(parameters["topRigRadius"]);

                if (parameters.ContainsKey("middleRigHeight"))
                    freeLook.m_Orbits[1].m_Height = float.Parse(parameters["middleRigHeight"]);
                if (parameters.ContainsKey("middleRigRadius"))
                    freeLook.m_Orbits[1].m_Radius = float.Parse(parameters["middleRigRadius"]);

                if (parameters.ContainsKey("bottomRigHeight"))
                    freeLook.m_Orbits[2].m_Height = float.Parse(parameters["bottomRigHeight"]);
                if (parameters.ContainsKey("bottomRigRadius"))
                    freeLook.m_Orbits[2].m_Radius = float.Parse(parameters["bottomRigRadius"]);

                // Configure axes
                if (parameters.ContainsKey("xAxisSpeed"))
                    freeLook.m_XAxis.m_MaxSpeed = float.Parse(parameters["xAxisSpeed"]);
                if (parameters.ContainsKey("yAxisSpeed"))
                    freeLook.m_YAxis.m_MaxSpeed = float.Parse(parameters["yAxisSpeed"]);

                // Priority
                if (parameters.ContainsKey("priority"))
                    freeLook.Priority = int.Parse(parameters["priority"]);

                Undo.RegisterCreatedObjectUndo(freeLookObj, "Create FreeLook Camera");
                Selection.activeGameObject = freeLookObj;

                // Success message
                string successMsg = $"FreeLook Camera '{name}' created successfully\n" +
                                  $"  Component: CinemachineFreeLook (CM2)\n" +
                                  $"  Following: {parameters["follow"]}\n" +
                                  $"  Orbits: Top, Middle, Bottom rigs configured";

                SynLog.Info($"[Synaptic] {successMsg}");
                return successMsg;
            }
            catch (Exception e)
            {
                return $"Error: Failed to create FreeLook Camera\n\n" +
                       $"Details: {e.Message}\n\n" +
                       $"Stack Trace:\n{e.StackTrace}\n\n" +
                       $"Common causes:\n" +
                       $"  - Cinemachine 2.x not installed (check Package Manager)\n" +
                       $"  - Invalid parameter values\n" +
                       $"  - Scene not properly loaded";
            }
#elif CINEMACHINE_3
            try
            {
                string name = parameters.ContainsKey("name") ? parameters["name"] : "CM FreeLook1";

                // In CM3, FreeLook is implemented as CinemachineCamera + CinemachineOrbitalFollow
                GameObject freeLookObj = new GameObject(name);
                var cam = freeLookObj.AddComponent<CinemachineCamera>();

                // Set Follow (REQUIRED)
                if (!parameters.ContainsKey("follow"))
                {
                    // Clean up the camera we just created
                    GameObject.DestroyImmediate(freeLookObj);

                    return $"Error: FreeLook camera creation failed\n\n" +
                           $"Reason: 'follow' parameter is required\n\n" +
                           $"FreeLook cameras must track a target GameObject.\n\n" +
                           $"Example usage:\n" +
                           $"  unity_create_freelook_camera(name='PlayerCamera', follow='Player')\n" +
                           $"  unity_create_freelook_camera(follow='Cube', priority=10)\n\n" +
                           $"Tip: Create the target GameObject first, then create the FreeLook camera";
                }

                var followTarget = GameObject.Find(parameters["follow"]);
                if (followTarget == null)
                {
                    // Clean up the camera we just created
                    GameObject.DestroyImmediate(freeLookObj);

                    // Provide helpful error with solutions
                    return $"Error: FreeLook camera creation failed\n\n" +
                           $"Reason: Follow target '{parameters["follow"]}' not found in scene\n\n" +
                           $"Solutions:\n" +
                           $"  1. Create a GameObject named '{parameters["follow"]}' first\n" +
                           $"  2. Check spelling - GameObject names are case-sensitive\n" +
                           $"  3. Check Hierarchy window for existing GameObjects\n\n" +
                           $"Example:\n" +
                           $"  unity_create_gameobject(name='Player', position='0,0,0')\n" +
                           $"  unity_create_freelook_camera(name='PlayerCamera', follow='Player')";
                }

                cam.Target.TrackingTarget = followTarget.transform;
                cam.Target.LookAtTarget = followTarget.transform; // Default is Follow target

                // Set LookAt (optional)
                if (parameters.ContainsKey("lookAt"))
                {
                    var lookAtTarget = GameObject.Find(parameters["lookAt"]);
                    if (lookAtTarget != null)
                    {
                        cam.Target.LookAtTarget = lookAtTarget.transform;
                    }
                }

                // Add Orbital Follow component for FreeLook behavior
                var orbital = cam.gameObject.AddComponent<CinemachineOrbitalFollow>();

                // Set ThreeRing orbit style
                orbital.OrbitStyle = CinemachineOrbitalFollow.OrbitStyles.ThreeRing;

                // Configure orbit radii (CM3 uses different structure)
                if (parameters.ContainsKey("topRigRadius"))
                {
                    float topRadius = float.Parse(parameters["topRigRadius"]);
                    orbital.Radius = topRadius;
                }
                else
                {
                    orbital.Radius = 5f; // Default radius
                }

                // Add Rotation Composer for proper FreeLook aiming (REQUIRED for FreeLook)
                var rotationComposer = cam.gameObject.AddComponent<CinemachineRotationComposer>();
                rotationComposer.Composition.ScreenPosition = new Vector2(0.5f, 0.5f); // Center

                // Add Input Axis Controller for user input
                var inputAxis = cam.gameObject.AddComponent<CinemachineInputAxisController>();

                // Note: CM3's InputAxisController speed configuration is done via Driver settings
                if (parameters.ContainsKey("xAxisSpeed") || parameters.ContainsKey("yAxisSpeed"))
                {
                    SynLog.Info("[Synaptic] CM3 FreeLook: Input axis speeds configured. Adjust via Inspector if needed.");
                }

                // Priority
                if (parameters.ContainsKey("priority"))
                {
                    cam.Priority = int.Parse(parameters["priority"]);
                }

                Undo.RegisterCreatedObjectUndo(freeLookObj, "Create FreeLook Camera");
                Selection.activeGameObject = freeLookObj;

                // Success message
                string successMsg = $"FreeLook Camera '{name}' created successfully\n" +
                                  $"  Components: CinemachineCamera, OrbitalFollow, RotationComposer, InputAxisController\n" +
                                  $"  Following: {parameters["follow"]}";

                SynLog.Info($"[Synaptic] {successMsg}");
                return successMsg;
            }
            catch (Exception e)
            {
                return $"Error: Failed to create FreeLook Camera\n\n" +
                       $"Details: {e.Message}\n\n" +
                       $"Stack Trace:\n{e.StackTrace}\n\n" +
                       $"Common causes:\n" +
                       $"  - Cinemachine 3.x not installed (check Package Manager)\n" +
                       $"  - Invalid parameter values\n" +
                       $"  - Scene not properly loaded";
            }
#else
            return "Error: Cinemachine package not installed. Please install via Package Manager.";
#endif
        }

        /// <summary>
        /// Setup Cinemachine Brain
        /// </summary>
        public static string SetupCinemachineBrain(Dictionary<string, string> parameters)
        {
#if CINEMACHINE_2
            try
            {
                string cameraName = parameters.ContainsKey("camera") ? parameters["camera"] : "Main Camera";

                Camera cam = Camera.main;
                if (cameraName != "Main Camera")
                {
                    var camObj = GameObject.Find(cameraName);
                    if (camObj != null)
                        cam = camObj.GetComponent<Camera>();
                }

                if (cam == null)
                {
                    return $"Error: Camera '{cameraName}' not found";
                }

                // Check for existing Brain
                var brain = cam.GetComponent<CinemachineBrain>();
                if (brain == null)
                {
                    brain = Undo.AddComponent<CinemachineBrain>(cam.gameObject);
                }

                // Blend settings
                if (parameters.ContainsKey("blendStyle"))
                {
                    brain.m_DefaultBlend.m_Style = ParseBlendStyle(parameters["blendStyle"]);
                }

                if (parameters.ContainsKey("defaultBlendTime"))
                {
                    brain.m_DefaultBlend.m_Time = float.Parse(parameters["defaultBlendTime"]);
                }

                // Update method
                if (parameters.ContainsKey("updateMethod"))
                {
                    brain.m_UpdateMethod = ParseUpdateMethod(parameters["updateMethod"]);
                }

                EditorUtility.SetDirty(brain);

                return $"Cinemachine Brain setup on '{cameraName}' with {brain.m_DefaultBlend.m_Style} blend";
            }
            catch (Exception e)
            {
                return $"Error: Failed to setup Cinemachine Brain - {e.Message}";
            }
#elif CINEMACHINE_3
            try
            {
                string cameraName = parameters.ContainsKey("camera") ? parameters["camera"] : "Main Camera";

                Camera cam = Camera.main;
                if (cameraName != "Main Camera")
                {
                    var camObj = GameObject.Find(cameraName);
                    if (camObj != null)
                        cam = camObj.GetComponent<Camera>();
                }

                if (cam == null)
                {
                    return $"Error: Camera '{cameraName}' not found";
                }

                // Check for existing Brain
                var brain = cam.GetComponent<CinemachineBrain>();
                if (brain == null)
                {
                    brain = Undo.AddComponent<CinemachineBrain>(cam.gameObject);
                }

                // Blend settings (no m_ prefix in CM3)
                if (parameters.ContainsKey("blendStyle"))
                {
                    brain.DefaultBlend.Style = ParseBlendStyle3(parameters["blendStyle"]);
                }

                if (parameters.ContainsKey("defaultBlendTime"))
                {
                    brain.DefaultBlend.Time = float.Parse(parameters["defaultBlendTime"]);
                }

                // Update method
                if (parameters.ContainsKey("updateMethod"))
                {
                    brain.UpdateMethod = ParseUpdateMethod3(parameters["updateMethod"]);
                }

                EditorUtility.SetDirty(brain);

                return $"Cinemachine Brain setup on '{cameraName}' with {brain.DefaultBlend.Style} blend";
            }
            catch (Exception e)
            {
                return $"Error: Failed to setup Cinemachine Brain - {e.Message}";
            }
#else
            return "Error: Cinemachine package not installed. Please install via Package Manager.";
#endif
        }

        /// <summary>
        /// Update Virtual Camera
        /// </summary>
        public static string UpdateVirtualCamera(Dictionary<string, string> parameters)
        {
#if CINEMACHINE_2
            try
            {
                if (!parameters.ContainsKey("camera"))
                {
                    return "Error: 'camera' parameter is required";
                }

                var vcamObj = GameObject.Find(parameters["camera"]);
                if (vcamObj == null)
                {
                    return $"Error: Virtual Camera '{parameters["camera"]}' not found";
                }

                var vcam = vcamObj.GetComponent<CinemachineVirtualCamera>();
                if (vcam == null)
                {
                    return $"Error: '{parameters["camera"]}' is not a Virtual Camera";
                }

                Undo.RecordObject(vcam, "Update Virtual Camera");

                // Update Priority
                if (parameters.ContainsKey("priority"))
                {
                    vcam.Priority = int.Parse(parameters["priority"]);
                }

                // Update Follow/LookAt
                if (parameters.ContainsKey("follow"))
                {
                    var followTarget = GameObject.Find(parameters["follow"]);
                    vcam.Follow = followTarget?.transform;
                }

                if (parameters.ContainsKey("lookAt"))
                {
                    var lookAtTarget = GameObject.Find(parameters["lookAt"]);
                    vcam.LookAt = lookAtTarget?.transform;
                }

                // Update Lens
                if (parameters.ContainsKey("fov"))
                {
                    vcam.m_Lens.FieldOfView = float.Parse(parameters["fov"]);
                }
                if (parameters.ContainsKey("nearClipPlane"))
                {
                    vcam.m_Lens.NearClipPlane = float.Parse(parameters["nearClipPlane"]);
                }
                if (parameters.ContainsKey("farClipPlane"))
                {
                    vcam.m_Lens.FarClipPlane = float.Parse(parameters["farClipPlane"]);
                }
                if (parameters.ContainsKey("dutch"))
                {
                    vcam.m_Lens.Dutch = float.Parse(parameters["dutch"]);
                }
                if (parameters.ContainsKey("orthographic"))
                {
                    vcam.m_Lens.Orthographic = bool.Parse(parameters["orthographic"]);
                }
                if (parameters.ContainsKey("orthographicSize"))
                {
                    vcam.m_Lens.OrthographicSize = float.Parse(parameters["orthographicSize"]);
                }

                // Change Body/Aim component
                if (parameters.ContainsKey("bodyType"))
                {
                    // Remove existing Body component
                    var existingBody = vcam.GetCinemachineComponent(CinemachineCore.Stage.Body);
                    if (existingBody != null)
                    {
                        Component.DestroyImmediate(existingBody);
                    }
                    // Add new Body component
                    AddBodyComponent(vcam, parameters["bodyType"], parameters);
                }

                if (parameters.ContainsKey("aimType"))
                {
                    // Remove existing Aim component
                    var existingAim = vcam.GetCinemachineComponent(CinemachineCore.Stage.Aim);
                    if (existingAim != null)
                    {
                        Component.DestroyImmediate(existingAim);
                    }
                    // Add new Aim component
                    AddAimComponent(vcam, parameters["aimType"], parameters);
                }

                // Update Damping (only if existing Transposer exists)
                if (parameters.ContainsKey("damping") && !parameters.ContainsKey("bodyType"))
                {
                    var transposer = vcam.GetCinemachineComponent<CinemachineTransposer>();
                    if (transposer != null)
                    {
                        var damping = ParseVector3(parameters["damping"]);
                        transposer.m_XDamping = damping.x;
                        transposer.m_YDamping = damping.y;
                        transposer.m_ZDamping = damping.z;
                    }
                }

                // Update Noise
                if (parameters.ContainsKey("noiseAmplitude") || parameters.ContainsKey("noiseFrequency") || parameters.ContainsKey("noiseProfile"))
                {
                    var noise = vcam.GetCinemachineComponent<CinemachineBasicMultiChannelPerlin>();
                    if (noise == null)
                    {
                        noise = vcam.AddCinemachineComponent<CinemachineBasicMultiChannelPerlin>();
                    }

                    if (parameters.ContainsKey("noiseProfile"))
                    {
                        NoiseSettings profile = GetNoiseProfile(parameters["noiseProfile"]);
                        if (profile != null)
                        {
                            noise.m_NoiseProfile = profile;
                        }
                    }

                    if (parameters.ContainsKey("noiseAmplitude"))
                    {
                        noise.m_AmplitudeGain = float.Parse(parameters["noiseAmplitude"]);
                    }

                    if (parameters.ContainsKey("noiseFrequency"))
                    {
                        noise.m_FrequencyGain = float.Parse(parameters["noiseFrequency"]);
                    }
                }

                EditorUtility.SetDirty(vcam);

                return $"Virtual Camera '{parameters["camera"]}' updated successfully";
            }
            catch (Exception e)
            {
                return $"Error: Failed to update Virtual Camera - {e.Message}";
            }
#elif CINEMACHINE_3
            try
            {
                if (!parameters.ContainsKey("camera"))
                {
                    return "Error: 'camera' parameter is required";
                }

                var vcamObj = GameObject.Find(parameters["camera"]);
                if (vcamObj == null)
                {
                    return $"Error: Virtual Camera '{parameters["camera"]}' not found";
                }

                var vcam = vcamObj.GetComponent<CinemachineCamera>();
                if (vcam == null)
                {
                    return $"Error: '{parameters["camera"]}' is not a Cinemachine Camera";
                }

                Undo.RecordObject(vcam, "Update Virtual Camera");

                // Update Priority
                if (parameters.ContainsKey("priority"))
                {
                    vcam.Priority.Value = int.Parse(parameters["priority"]);
                }

                // Update Follow/LookAt (Target in CM3)
                if (parameters.ContainsKey("follow"))
                {
                    var followTarget = GameObject.Find(parameters["follow"]);
                    vcam.Target.TrackingTarget = followTarget?.transform;
                }

                if (parameters.ContainsKey("lookAt"))
                {
                    var lookAtTarget = GameObject.Find(parameters["lookAt"]);
                    vcam.Target.LookAtTarget = lookAtTarget?.transform;
                }

                // Update Lens (no m_ prefix in CM3)
                if (parameters.ContainsKey("fov"))
                {
                    vcam.Lens.FieldOfView = float.Parse(parameters["fov"]);
                }
                if (parameters.ContainsKey("nearClipPlane"))
                {
                    vcam.Lens.NearClipPlane = float.Parse(parameters["nearClipPlane"]);
                }
                if (parameters.ContainsKey("farClipPlane"))
                {
                    vcam.Lens.FarClipPlane = float.Parse(parameters["farClipPlane"]);
                }
                if (parameters.ContainsKey("dutch"))
                {
                    vcam.Lens.Dutch = float.Parse(parameters["dutch"]);
                }
                if (parameters.ContainsKey("orthographic"))
                {
                    bool isOrthographic = bool.Parse(parameters["orthographic"]);
                    vcam.Lens.ModeOverride = isOrthographic
                        ? LensSettings.OverrideModes.Orthographic
                        : LensSettings.OverrideModes.None;
                }
                if (parameters.ContainsKey("orthographicSize"))
                {
                    vcam.Lens.OrthographicSize = float.Parse(parameters["orthographicSize"]);
                }

                // Change Body/Aim component (CM3 architecture is different)
                if (parameters.ContainsKey("bodyType"))
                {
                    // Remove existing follow components
                    var existingFollow = vcam.GetComponent<CinemachineFollow>();
                    var existingOrbital = vcam.GetComponent<CinemachineOrbitalFollow>();
                    var existingPosition = vcam.GetComponent<CinemachinePositionComposer>();

                    if (existingFollow != null) Component.DestroyImmediate(existingFollow);
                    if (existingOrbital != null) Component.DestroyImmediate(existingOrbital);
                    if (existingPosition != null) Component.DestroyImmediate(existingPosition);

                    // Add new Body component
                    AddBodyComponent3(vcam, parameters["bodyType"], parameters);
                }

                if (parameters.ContainsKey("aimType"))
                {
                    // Remove existing aim components
                    var existingRotation = vcam.GetComponent<CinemachineRotationComposer>();
                    if (existingRotation != null) Component.DestroyImmediate(existingRotation);

                    // Add new Aim component
                    AddAimComponent3(vcam, parameters["aimType"], parameters);
                }

                // Update Noise
                if (parameters.ContainsKey("noiseAmplitude") || parameters.ContainsKey("noiseFrequency") || parameters.ContainsKey("noiseProfile"))
                {
                    var noise = vcam.GetComponent<CinemachineBasicMultiChannelPerlin>();
                    if (noise == null)
                    {
                        noise = vcam.gameObject.AddComponent<CinemachineBasicMultiChannelPerlin>();
                    }

                    if (parameters.ContainsKey("noiseProfile"))
                    {
                        NoiseSettings profile = GetNoiseProfile(parameters["noiseProfile"]);
                        if (profile != null)
                        {
                            noise.NoiseProfile = profile;
                        }
                    }

                    if (parameters.ContainsKey("noiseAmplitude"))
                    {
                        noise.AmplitudeGain = float.Parse(parameters["noiseAmplitude"]);
                    }

                    if (parameters.ContainsKey("noiseFrequency"))
                    {
                        noise.FrequencyGain = float.Parse(parameters["noiseFrequency"]);
                    }
                }

                EditorUtility.SetDirty(vcam);

                return $"Virtual Camera '{parameters["camera"]}' updated successfully";
            }
            catch (Exception e)
            {
                return $"Error: Failed to update Virtual Camera - {e.Message}";
            }
#else
            return "Error: Cinemachine package not installed. Please install via Package Manager.";
#endif
        }

        /// <summary>
        /// Create State-Driven Camera
        /// </summary>
        public static string CreateStateDrivenCamera(Dictionary<string, string> parameters)
        {
#if CINEMACHINE_2
            try
            {
                string name = parameters.ContainsKey("name") ? parameters["name"] : "CM StateDriven1";

                GameObject sdCameraObj = new GameObject(name);
                var stateDriven = sdCameraObj.AddComponent<CinemachineStateDrivenCamera>();

                // Set Animator (required)
                if (!parameters.ContainsKey("animatedTarget"))
                {
                    return "Error: 'animatedTarget' parameter is required for State-Driven camera";
                }

                var animTarget = GameObject.Find(parameters["animatedTarget"]);
                if (animTarget == null)
                {
                    return $"Error: Animated target '{parameters["animatedTarget"]}' not found";
                }

                var animator = animTarget.GetComponent<Animator>();
                if (animator == null)
                {
                    return $"Error: '{parameters["animatedTarget"]}' does not have an Animator component";
                }

                stateDriven.m_AnimatedTarget = animator;

                // Set Layer Index
                if (parameters.ContainsKey("layerIndex"))
                {
                    stateDriven.m_LayerIndex = int.Parse(parameters["layerIndex"]);
                }

                // Set Follow/LookAt
                if (parameters.ContainsKey("follow"))
                {
                    var followTarget = GameObject.Find(parameters["follow"]);
                    if (followTarget != null)
                    {
                        stateDriven.Follow = followTarget.transform;
                    }
                }

                if (parameters.ContainsKey("lookAt"))
                {
                    var lookAtTarget = GameObject.Find(parameters["lookAt"]);
                    if (lookAtTarget != null)
                    {
                        stateDriven.LookAt = lookAtTarget.transform;
                    }
                }

                // Priority
                if (parameters.ContainsKey("priority"))
                {
                    stateDriven.Priority = int.Parse(parameters["priority"]);
                }

                Undo.RegisterCreatedObjectUndo(sdCameraObj, "Create State-Driven Camera");
                Selection.activeGameObject = sdCameraObj;

                return $"State-Driven Camera '{name}' created successfully, driven by '{parameters["animatedTarget"]}'";
            }
            catch (Exception e)
            {
                return $"Error: Failed to create State-Driven Camera - {e.Message}";
            }
#elif CINEMACHINE_3
            try
            {
                string name = parameters.ContainsKey("name") ? parameters["name"] : "CM StateDriven1";

                GameObject sdCameraObj = new GameObject(name);
                var stateDriven = sdCameraObj.AddComponent<CinemachineStateDrivenCamera>();

                // Set Animator (required)
                if (!parameters.ContainsKey("animatedTarget"))
                {
                    return "Error: 'animatedTarget' parameter is required for State-Driven camera";
                }

                var animTarget = GameObject.Find(parameters["animatedTarget"]);
                if (animTarget == null)
                {
                    return $"Error: Animated target '{parameters["animatedTarget"]}' not found";
                }

                var animator = animTarget.GetComponent<Animator>();
                if (animator == null)
                {
                    return $"Error: '{parameters["animatedTarget"]}' does not have an Animator component";
                }

                stateDriven.AnimatedTarget = animator;

                // Set Layer Index (no m_ prefix in CM3)
                if (parameters.ContainsKey("layerIndex"))
                {
                    stateDriven.LayerIndex = int.Parse(parameters["layerIndex"]);
                }

                // Set Follow/LookAt
                if (parameters.ContainsKey("follow"))
                {
                    var followTarget = GameObject.Find(parameters["follow"]);
                    if (followTarget != null)
                    {
                        stateDriven.Follow = followTarget.transform;
                    }
                }

                if (parameters.ContainsKey("lookAt"))
                {
                    var lookAtTarget = GameObject.Find(parameters["lookAt"]);
                    if (lookAtTarget != null)
                    {
                        stateDriven.LookAt = lookAtTarget.transform;
                    }
                }

                // Priority
                if (parameters.ContainsKey("priority"))
                {
                    stateDriven.Priority = int.Parse(parameters["priority"]);
                }

                Undo.RegisterCreatedObjectUndo(sdCameraObj, "Create State-Driven Camera");
                Selection.activeGameObject = sdCameraObj;

                return $"State-Driven Camera '{name}' created successfully, driven by '{parameters["animatedTarget"]}'";
            }
            catch (Exception e)
            {
                return $"Error: Failed to create State-Driven Camera - {e.Message}";
            }
#else
            return "Error: Cinemachine package not installed. Please install via Package Manager.";
#endif
        }

        /// <summary>
        /// Create Clear Shot Camera
        /// </summary>
        public static string CreateClearShotCamera(Dictionary<string, string> parameters)
        {
#if CINEMACHINE_2
            try
            {
                string name = parameters.ContainsKey("name") ? parameters["name"] : "CM ClearShot1";

                GameObject clearShotObj = new GameObject(name);
                var clearShot = clearShotObj.AddComponent<CinemachineClearShot>();

                // Set Follow/LookAt
                if (parameters.ContainsKey("follow"))
                {
                    var followTarget = GameObject.Find(parameters["follow"]);
                    if (followTarget != null)
                    {
                        clearShot.Follow = followTarget.transform;
                    }
                }

                if (parameters.ContainsKey("lookAt"))
                {
                    var lookAtTarget = GameObject.Find(parameters["lookAt"]);
                    if (lookAtTarget != null)
                    {
                        clearShot.LookAt = lookAtTarget.transform;
                    }
                }

                // Priority
                if (parameters.ContainsKey("priority"))
                {
                    clearShot.Priority = int.Parse(parameters["priority"]);
                }

                Undo.RegisterCreatedObjectUndo(clearShotObj, "Create Clear Shot Camera");
                Selection.activeGameObject = clearShotObj;

                return $"Clear Shot Camera '{name}' created successfully. Add child Virtual Cameras to create shot candidates.";
            }
            catch (Exception e)
            {
                return $"Error: Failed to create Clear Shot Camera - {e.Message}";
            }
#elif CINEMACHINE_3
            try
            {
                string name = parameters.ContainsKey("name") ? parameters["name"] : "CM ClearShot1";

                GameObject clearShotObj = new GameObject(name);
                var clearShot = clearShotObj.AddComponent<CinemachineClearShot>();

                // Set Follow/LookAt
                if (parameters.ContainsKey("follow"))
                {
                    var followTarget = GameObject.Find(parameters["follow"]);
                    if (followTarget != null)
                    {
                        clearShot.Follow = followTarget.transform;
                    }
                }

                if (parameters.ContainsKey("lookAt"))
                {
                    var lookAtTarget = GameObject.Find(parameters["lookAt"]);
                    if (lookAtTarget != null)
                    {
                        clearShot.LookAt = lookAtTarget.transform;
                    }
                }

                // Priority
                if (parameters.ContainsKey("priority"))
                {
                    clearShot.Priority = int.Parse(parameters["priority"]);
                }

                Undo.RegisterCreatedObjectUndo(clearShotObj, "Create Clear Shot Camera");
                Selection.activeGameObject = clearShotObj;

                return $"Clear Shot Camera '{name}' created successfully. Add child Virtual Cameras to create shot candidates.";
            }
            catch (Exception e)
            {
                return $"Error: Failed to create Clear Shot Camera - {e.Message}";
            }
#else
            return "Error: Cinemachine package not installed. Please install via Package Manager.";
#endif
        }

        /// <summary>
        /// Create Impulse Source
        /// </summary>
        public static string CreateImpulseSource(Dictionary<string, string> parameters)
        {
#if CINEMACHINE_2
            try
            {
                if (!parameters.ContainsKey("gameObject"))
                {
                    return "Error: 'gameObject' parameter is required";
                }

                var targetObj = GameObject.Find(parameters["gameObject"]);
                if (targetObj == null)
                {
                    return $"Error: GameObject '{parameters["gameObject"]}' not found";
                }

                // Add Impulse Source
                var impulseSource = Undo.AddComponent<CinemachineImpulseSource>(targetObj);

                // Set Raw Signal
                if (parameters.ContainsKey("amplitudeGain"))
                {
                    impulseSource.m_ImpulseDefinition.m_AmplitudeGain = float.Parse(parameters["amplitudeGain"]);
                }

                if (parameters.ContainsKey("frequencyGain"))
                {
                    impulseSource.m_ImpulseDefinition.m_FrequencyGain = float.Parse(parameters["frequencyGain"]);
                }

                if (parameters.ContainsKey("impulseDuration"))
                {
                    impulseSource.m_ImpulseDefinition.m_TimeEnvelope.m_SustainTime = float.Parse(parameters["impulseDuration"]);
                }

                // Set Velocity
                if (parameters.ContainsKey("defaultVelocity"))
                {
                    var velocity = ParseVector3(parameters["defaultVelocity"]);
                    impulseSource.m_DefaultVelocity = velocity;
                }

                EditorUtility.SetDirty(targetObj);

                return $"Impulse Source added to '{parameters["gameObject"]}'";
            }
            catch (Exception e)
            {
                return $"Error: Failed to create Impulse Source - {e.Message}";
            }
#elif CINEMACHINE_3
            try
            {
                if (!parameters.ContainsKey("gameObject"))
                {
                    return "Error: 'gameObject' parameter is required";
                }

                var targetObj = GameObject.Find(parameters["gameObject"]);
                if (targetObj == null)
                {
                    return $"Error: GameObject '{parameters["gameObject"]}' not found";
                }

                // Add Impulse Source (no m_ prefix in CM3)
                var impulseSource = Undo.AddComponent<CinemachineImpulseSource>(targetObj);

                // Set Impulse Definition properties
                if (parameters.ContainsKey("amplitudeGain") || parameters.ContainsKey("frequencyGain") || parameters.ContainsKey("impulseDuration"))
                {
                    var impulseDef = impulseSource.ImpulseDefinition;

                    if (parameters.ContainsKey("amplitudeGain"))
                    {
                        impulseDef.AmplitudeGain = float.Parse(parameters["amplitudeGain"]);
                    }

                    if (parameters.ContainsKey("frequencyGain"))
                    {
                        impulseDef.FrequencyGain = float.Parse(parameters["frequencyGain"]);
                    }

                    if (parameters.ContainsKey("impulseDuration"))
                    {
                        impulseDef.ImpulseDuration = float.Parse(parameters["impulseDuration"]);
                    }

                    impulseSource.ImpulseDefinition = impulseDef;
                }

                // Set Velocity
                if (parameters.ContainsKey("defaultVelocity"))
                {
                    var velocity = ParseVector3(parameters["defaultVelocity"]);
                    impulseSource.DefaultVelocity = velocity;
                }

                EditorUtility.SetDirty(targetObj);

                return $"Impulse Source added to '{parameters["gameObject"]}'";
            }
            catch (Exception e)
            {
                return $"Error: Failed to create Impulse Source - {e.Message}";
            }
#else
            return "Error: Cinemachine package not installed. Please install via Package Manager.";
#endif
        }

        /// <summary>
        /// Add Impulse Listener
        /// </summary>
        public static string AddImpulseListener(Dictionary<string, string> parameters)
        {
#if CINEMACHINE_2
            try
            {
                if (!parameters.ContainsKey("camera"))
                {
                    return "Error: 'camera' parameter is required";
                }

                var vcamObj = GameObject.Find(parameters["camera"]);
                if (vcamObj == null)
                {
                    return $"Error: Virtual Camera '{parameters["camera"]}' not found";
                }

                var vcam = vcamObj.GetComponent<CinemachineVirtualCamera>();
                if (vcam == null)
                {
                    return $"Error: '{parameters["camera"]}' is not a Virtual Camera";
                }

                // Add Impulse Listener
                var listener = vcam.gameObject.AddComponent<CinemachineImpulseListener>();

                // Set Gain
                if (parameters.ContainsKey("gain"))
                {
                    listener.m_Gain = float.Parse(parameters["gain"]);
                }

                // Set Use 2D Distance
                if (parameters.ContainsKey("use2DDistance"))
                {
                    listener.m_Use2DDistance = bool.Parse(parameters["use2DDistance"]);
                }

                // Reaction Settings
                if (parameters.ContainsKey("amplitudeGain"))
                {
                    listener.m_ReactionSettings.m_AmplitudeGain = float.Parse(parameters["amplitudeGain"]);
                }

                if (parameters.ContainsKey("frequencyGain"))
                {
                    listener.m_ReactionSettings.m_FrequencyGain = float.Parse(parameters["frequencyGain"]);
                }

                EditorUtility.SetDirty(vcam);

                return $"Impulse Listener added to '{parameters["camera"]}'";
            }
            catch (Exception e)
            {
                return $"Error: Failed to add Impulse Listener - {e.Message}";
            }
#elif CINEMACHINE_3
            try
            {
                if (!parameters.ContainsKey("camera"))
                {
                    return "Error: 'camera' parameter is required";
                }

                var vcamObj = GameObject.Find(parameters["camera"]);
                if (vcamObj == null)
                {
                    return $"Error: Virtual Camera '{parameters["camera"]}' not found";
                }

                var vcam = vcamObj.GetComponent<CinemachineCamera>();
                if (vcam == null)
                {
                    return $"Error: '{parameters["camera"]}' is not a Cinemachine Camera";
                }

                // Add Impulse Listener (no m_ prefix in CM3)
                var listener = vcam.gameObject.AddComponent<CinemachineImpulseListener>();

                // Set Gain
                if (parameters.ContainsKey("gain"))
                {
                    listener.Gain = float.Parse(parameters["gain"]);
                }

                // Set Use 2D Distance
                if (parameters.ContainsKey("use2DDistance"))
                {
                    listener.Use2DDistance = bool.Parse(parameters["use2DDistance"]);
                }

                // Reaction Settings
                if (parameters.ContainsKey("amplitudeGain"))
                {
                    listener.ReactionSettings.AmplitudeGain = float.Parse(parameters["amplitudeGain"]);
                }

                if (parameters.ContainsKey("frequencyGain"))
                {
                    listener.ReactionSettings.FrequencyGain = float.Parse(parameters["frequencyGain"]);
                }

                EditorUtility.SetDirty(vcam);

                return $"Impulse Listener added to '{parameters["camera"]}'";
            }
            catch (Exception e)
            {
                return $"Error: Failed to add Impulse Listener - {e.Message}";
            }
#else
            return "Error: Cinemachine package not installed. Please install via Package Manager.";
#endif
        }

        /// <summary>
        /// Create Blend List Camera
        /// </summary>
        public static string CreateBlendListCamera(Dictionary<string, string> parameters)
        {
#if CINEMACHINE_2
            try
            {
                string name = parameters.ContainsKey("name") ? parameters["name"] : "CM BlendList";

                // Create Blend List Camera object
                GameObject blendListObj = new GameObject(name);
                var blendListCam = blendListObj.AddComponent<CinemachineBlendListCamera>();

                // Set priority
                if (parameters.ContainsKey("priority"))
                {
                    blendListCam.Priority = int.Parse(parameters["priority"]);
                }

                // Set Follow/LookAt
                if (parameters.ContainsKey("follow"))
                {
                    var followObj = GameObject.Find(parameters["follow"]);
                    if (followObj != null)
                    {
                        blendListCam.Follow = followObj.transform;
                    }
                }

                if (parameters.ContainsKey("lookAt"))
                {
                    var lookAtObj = GameObject.Find(parameters["lookAt"]);
                    if (lookAtObj != null)
                    {
                        blendListCam.LookAt = lookAtObj.transform;
                    }
                }

                // Set Loop
                if (parameters.ContainsKey("loop"))
                {
                    blendListCam.m_Loop = bool.Parse(parameters["loop"]);
                }

                // Set Show Debug Text
                if (parameters.ContainsKey("showDebugText"))
                {
                    blendListCam.m_ShowDebugText = bool.Parse(parameters["showDebugText"]);
                }

                Undo.RegisterCreatedObjectUndo(blendListObj, "Create Blend List Camera");
                Selection.activeGameObject = blendListObj;

                return $"Blend List Camera '{name}' created successfully";
            }
            catch (Exception e)
            {
                return $"Error: Failed to create Blend List Camera - {e.Message}";
            }
#elif CINEMACHINE_3
            try
            {
                string name = parameters.ContainsKey("name") ? parameters["name"] : "CM Sequencer";

                // In CM3, BlendListCamera is renamed to SequencerCamera
                GameObject sequencerObj = new GameObject(name);
                var sequencer = sequencerObj.AddComponent<CinemachineSequencerCamera>();

                // Set priority
                if (parameters.ContainsKey("priority"))
                {
                    sequencer.Priority = int.Parse(parameters["priority"]);
                }

                // Set Follow/LookAt
                if (parameters.ContainsKey("follow"))
                {
                    var followObj = GameObject.Find(parameters["follow"]);
                    if (followObj != null)
                    {
                        sequencer.Follow = followObj.transform;
                    }
                }

                if (parameters.ContainsKey("lookAt"))
                {
                    var lookAtObj = GameObject.Find(parameters["lookAt"]);
                    if (lookAtObj != null)
                    {
                        sequencer.LookAt = lookAtObj.transform;
                    }
                }

                // Set Loop (no m_ prefix in CM3)
                if (parameters.ContainsKey("loop"))
                {
                    sequencer.Loop = bool.Parse(parameters["loop"]);
                }

                Undo.RegisterCreatedObjectUndo(sequencerObj, "Create Sequencer Camera");
                Selection.activeGameObject = sequencerObj;

                return $"Sequencer Camera (CM3 BlendList) '{name}' created successfully";
            }
            catch (Exception e)
            {
                return $"Error: Failed to create Blend List Camera - {e.Message}";
            }
#else
            return "Error: Cinemachine package not installed. Please install via Package Manager.";
#endif
        }

        /// <summary>
        /// Create Target Group
        /// </summary>
        public static string CreateTargetGroup(Dictionary<string, string> parameters)
        {
#if CINEMACHINE_2
            try
            {
                string name = parameters.ContainsKey("name") ? parameters["name"] : "CM TargetGroup";

                // Create Target Group object
                GameObject targetGroupObj = new GameObject(name);
                var targetGroup = targetGroupObj.AddComponent<CinemachineTargetGroup>();

                // Set Position Mode
                if (parameters.ContainsKey("positionMode"))
                {
                    targetGroup.m_PositionMode = ParseTargetGroupPositionMode(parameters["positionMode"]);
                }

                // Set Rotation Mode
                if (parameters.ContainsKey("rotationMode"))
                {
                    targetGroup.m_RotationMode = ParseTargetGroupRotationMode(parameters["rotationMode"]);
                }

                // Set Update Method
                if (parameters.ContainsKey("updateMethod"))
                {
                    targetGroup.m_UpdateMethod = ParseTargetGroupUpdateMethod(parameters["updateMethod"]);
                }

                Undo.RegisterCreatedObjectUndo(targetGroupObj, "Create Target Group");
                Selection.activeGameObject = targetGroupObj;

                return $"Target Group '{name}' created successfully";
            }
            catch (Exception e)
            {
                return $"Error: Failed to create Target Group - {e.Message}";
            }
#elif CINEMACHINE_3
            try
            {
                string name = parameters.ContainsKey("name") ? parameters["name"] : "CM TargetGroup";

                // Create Target Group object (same API in CM3)
                GameObject targetGroupObj = new GameObject(name);
                var targetGroup = targetGroupObj.AddComponent<CinemachineTargetGroup>();

                // Set Position Mode (no m_ prefix in CM3)
                if (parameters.ContainsKey("positionMode"))
                {
                    targetGroup.PositionMode = ParseTargetGroupPositionMode3(parameters["positionMode"]);
                }

                // Set Rotation Mode
                if (parameters.ContainsKey("rotationMode"))
                {
                    targetGroup.RotationMode = ParseTargetGroupRotationMode3(parameters["rotationMode"]);
                }

                // Set Update Method
                if (parameters.ContainsKey("updateMethod"))
                {
                    targetGroup.UpdateMethod = ParseTargetGroupUpdateMethod3(parameters["updateMethod"]);
                }

                Undo.RegisterCreatedObjectUndo(targetGroupObj, "Create Target Group");
                Selection.activeGameObject = targetGroupObj;

                return $"Target Group '{name}' created successfully";
            }
            catch (Exception e)
            {
                return $"Error: Failed to create Target Group - {e.Message}";
            }
#else
            return "Error: Cinemachine package not installed. Please install via Package Manager.";
#endif
        }

        /// <summary>
        /// Add Target to Target Group
        /// </summary>
        public static string AddTargetToGroup(Dictionary<string, string> parameters)
        {
#if CINEMACHINE_2
            try
            {
                if (!parameters.ContainsKey("targetGroup"))
                {
                    return "Error: 'targetGroup' parameter is required";
                }

                if (!parameters.ContainsKey("target"))
                {
                    return "Error: 'target' parameter is required";
                }

                var targetGroupObj = GameObject.Find(parameters["targetGroup"]);
                if (targetGroupObj == null)
                {
                    return $"Error: Target Group '{parameters["targetGroup"]}' not found";
                }

                var targetGroup = targetGroupObj.GetComponent<CinemachineTargetGroup>();
                if (targetGroup == null)
                {
                    return $"Error: '{parameters["targetGroup"]}' is not a Target Group";
                }

                var targetObj = GameObject.Find(parameters["target"]);
                if (targetObj == null)
                {
                    return $"Error: Target GameObject '{parameters["target"]}' not found";
                }

                // Set Weight
                float weight = 1.0f;
                if (parameters.ContainsKey("weight"))
                {
                    weight = float.Parse(parameters["weight"]);
                }

                // Set Radius
                float radius = 1.0f;
                if (parameters.ContainsKey("radius"))
                {
                    radius = float.Parse(parameters["radius"]);
                }

                // Add target
                targetGroup.AddMember(targetObj.transform, weight, radius);

                EditorUtility.SetDirty(targetGroup);

                return $"Target '{parameters["target"]}' added to group '{parameters["targetGroup"]}'";
            }
            catch (Exception e)
            {
                return $"Error: Failed to add target to group - {e.Message}";
            }
#elif CINEMACHINE_3
            try
            {
                if (!parameters.ContainsKey("targetGroup"))
                {
                    return "Error: 'targetGroup' parameter is required";
                }

                if (!parameters.ContainsKey("target"))
                {
                    return "Error: 'target' parameter is required";
                }

                var targetGroupObj = GameObject.Find(parameters["targetGroup"]);
                if (targetGroupObj == null)
                {
                    return $"Error: Target Group '{parameters["targetGroup"]}' not found";
                }

                var targetGroup = targetGroupObj.GetComponent<CinemachineTargetGroup>();
                if (targetGroup == null)
                {
                    return $"Error: '{parameters["targetGroup"]}' is not a Target Group";
                }

                var targetObj = GameObject.Find(parameters["target"]);
                if (targetObj == null)
                {
                    return $"Error: Target GameObject '{parameters["target"]}' not found";
                }

                // Set Weight
                float weight = 1.0f;
                if (parameters.ContainsKey("weight"))
                {
                    weight = float.Parse(parameters["weight"]);
                }

                // Set Radius
                float radius = 1.0f;
                if (parameters.ContainsKey("radius"))
                {
                    radius = float.Parse(parameters["radius"]);
                }

                // Add target (same API in CM3)
                targetGroup.AddMember(targetObj.transform, weight, radius);

                EditorUtility.SetDirty(targetGroup);

                return $"Target '{parameters["target"]}' added to group '{parameters["targetGroup"]}'";
            }
            catch (Exception e)
            {
                return $"Error: Failed to add target to group - {e.Message}";
            }
#else
            return "Error: Cinemachine package not installed. Please install via Package Manager.";
#endif
        }

        /// <summary>
        /// Set Virtual Camera priority
        /// </summary>
        public static string SetCameraPriority(Dictionary<string, string> parameters)
        {
#if CINEMACHINE_2
            try
            {
                if (!parameters.ContainsKey("camera"))
                {
                    return "Error: 'camera' parameter is required";
                }

                if (!parameters.ContainsKey("priority"))
                {
                    return "Error: 'priority' parameter is required";
                }

                var cameraObj = GameObject.Find(parameters["camera"]);
                if (cameraObj == null)
                {
                    return $"Error: Camera '{parameters["camera"]}' not found";
                }

                var vcam = cameraObj.GetComponent<CinemachineVirtualCameraBase>();
                if (vcam == null)
                {
                    return $"Error: '{parameters["camera"]}' is not a Cinemachine camera";
                }

                int priority = int.Parse(parameters["priority"]);
                vcam.Priority = priority;

                EditorUtility.SetDirty(vcam);

                return $"Camera '{parameters["camera"]}' priority set to {priority}";
            }
            catch (Exception e)
            {
                return $"Error: Failed to set camera priority - {e.Message}";
            }
#elif CINEMACHINE_3
            try
            {
                if (!parameters.ContainsKey("camera"))
                {
                    return "Error: 'camera' parameter is required";
                }

                if (!parameters.ContainsKey("priority"))
                {
                    return "Error: 'priority' parameter is required";
                }

                var cameraObj = GameObject.Find(parameters["camera"]);
                if (cameraObj == null)
                {
                    return $"Error: Camera '{parameters["camera"]}' not found";
                }

                var vcam = cameraObj.GetComponent<CinemachineVirtualCameraBase>();
                if (vcam == null)
                {
                    return $"Error: '{parameters["camera"]}' is not a Cinemachine camera";
                }

                int priority = int.Parse(parameters["priority"]);
                vcam.Priority = priority;

                EditorUtility.SetDirty(vcam);

                return $"Camera '{parameters["camera"]}' priority set to {priority}";
            }
            catch (Exception e)
            {
                return $"Error: Failed to set camera priority - {e.Message}";
            }
#else
            return "Error: Cinemachine package not installed. Please install via Package Manager.";
#endif
        }

        /// <summary>
        /// Toggle Virtual Camera enabled/disabled
        /// </summary>
        public static string SetCameraEnabled(Dictionary<string, string> parameters)
        {
#if CINEMACHINE_2
            try
            {
                if (!parameters.ContainsKey("camera"))
                {
                    return "Error: 'camera' parameter is required";
                }

                if (!parameters.ContainsKey("enabled"))
                {
                    return "Error: 'enabled' parameter is required";
                }

                var cameraObj = GameObject.Find(parameters["camera"]);
                if (cameraObj == null)
                {
                    return $"Error: Camera '{parameters["camera"]}' not found";
                }

                var vcam = cameraObj.GetComponent<CinemachineVirtualCameraBase>();
                if (vcam == null)
                {
                    return $"Error: '{parameters["camera"]}' is not a Cinemachine camera";
                }

                bool enabled = bool.Parse(parameters["enabled"]);
                vcam.enabled = enabled;

                EditorUtility.SetDirty(vcam);

                return $"Camera '{parameters["camera"]}' {(enabled ? "enabled" : "disabled")}";
            }
            catch (Exception e)
            {
                return $"Error: Failed to set camera enabled state - {e.Message}";
            }
#elif CINEMACHINE_3
            try
            {
                if (!parameters.ContainsKey("camera"))
                {
                    return "Error: 'camera' parameter is required";
                }

                if (!parameters.ContainsKey("enabled"))
                {
                    return "Error: 'enabled' parameter is required";
                }

                var cameraObj = GameObject.Find(parameters["camera"]);
                if (cameraObj == null)
                {
                    return $"Error: Camera '{parameters["camera"]}' not found";
                }

                var vcam = cameraObj.GetComponent<CinemachineVirtualCameraBase>();
                if (vcam == null)
                {
                    return $"Error: '{parameters["camera"]}' is not a Cinemachine camera";
                }

                bool enabled = bool.Parse(parameters["enabled"]);
                vcam.enabled = enabled;

                EditorUtility.SetDirty(vcam);

                return $"Camera '{parameters["camera"]}' {(enabled ? "enabled" : "disabled")}";
            }
            catch (Exception e)
            {
                return $"Error: Failed to set camera enabled state - {e.Message}";
            }
#else
            return "Error: Cinemachine package not installed. Please install via Package Manager.";
#endif
        }

        /// <summary>
        /// Create Mixing Camera
        /// </summary>
        public static string CreateMixingCamera(Dictionary<string, string> parameters)
        {
#if CINEMACHINE_2
            try
            {
                string name = parameters.ContainsKey("name") ? parameters["name"] : "CM MixingCamera";

                // Create Mixing Camera object
                GameObject mixingCameraObj = new GameObject(name);
                var mixingCamera = mixingCameraObj.AddComponent<CinemachineMixingCamera>();

                // Set priority
                if (parameters.ContainsKey("priority"))
                {
                    mixingCamera.Priority = int.Parse(parameters["priority"]);
                }

                // Set Weight（weight0〜weight7）
                for (int i = 0; i < 8; i++)
                {
                    string weightKey = $"weight{i}";
                    if (parameters.ContainsKey(weightKey))
                    {
                        float weight = float.Parse(parameters[weightKey]);
                        mixingCamera.SetWeight(i, weight);
                    }
                }

                Undo.RegisterCreatedObjectUndo(mixingCameraObj, "Create Mixing Camera");
                Selection.activeGameObject = mixingCameraObj;

                return $"Mixing Camera '{name}' created successfully";
            }
            catch (Exception e)
            {
                return $"Error: Failed to create Mixing Camera - {e.Message}";
            }
#elif CINEMACHINE_3
            try
            {
                string name = parameters.ContainsKey("name") ? parameters["name"] : "CM MixingCamera";

                // Create Mixing Camera object (same component in CM3)
                GameObject mixingCameraObj = new GameObject(name);
                var mixingCamera = mixingCameraObj.AddComponent<CinemachineMixingCamera>();

                // Set priority
                if (parameters.ContainsKey("priority"))
                {
                    mixingCamera.Priority = int.Parse(parameters["priority"]);
                }

                // Set Weight（weight0〜weight7）
                for (int i = 0; i < 8; i++)
                {
                    string weightKey = $"weight{i}";
                    if (parameters.ContainsKey(weightKey))
                    {
                        float weight = float.Parse(parameters[weightKey]);
                        mixingCamera.SetWeight(i, weight);
                    }
                }

                Undo.RegisterCreatedObjectUndo(mixingCameraObj, "Create Mixing Camera");
                Selection.activeGameObject = mixingCameraObj;

                return $"Mixing Camera '{name}' created successfully";
            }
            catch (Exception e)
            {
                return $"Error: Failed to create Mixing Camera - {e.Message}";
            }
#else
            return "Error: Cinemachine package not installed. Please install via Package Manager.";
#endif
        }

        /// <summary>
        /// Update Camera Follow/LookAt targets
        /// </summary>
        public static string UpdateCameraTarget(Dictionary<string, string> parameters)
        {
#if CINEMACHINE_2
            try
            {
                if (!parameters.ContainsKey("camera"))
                {
                    return "Error: 'camera' parameter is required";
                }

                var cameraObj = GameObject.Find(parameters["camera"]);
                if (cameraObj == null)
                {
                    return $"Error: Camera '{parameters["camera"]}' not found";
                }

                var vcam = cameraObj.GetComponent<CinemachineVirtualCameraBase>();
                if (vcam == null)
                {
                    return $"Error: '{parameters["camera"]}' is not a Cinemachine camera";
                }

                // Follow settings
                if (parameters.ContainsKey("follow"))
                {
                    if (string.IsNullOrEmpty(parameters["follow"]))
                    {
                        vcam.Follow = null;
                    }
                    else
                    {
                        var followObj = GameObject.Find(parameters["follow"]);
                        if (followObj != null)
                        {
                            vcam.Follow = followObj.transform;
                        }
                        else
                        {
                            return $"Error: Follow target '{parameters["follow"]}' not found";
                        }
                    }
                }

                // LookAt settings
                if (parameters.ContainsKey("lookAt"))
                {
                    if (string.IsNullOrEmpty(parameters["lookAt"]))
                    {
                        vcam.LookAt = null;
                    }
                    else
                    {
                        var lookAtObj = GameObject.Find(parameters["lookAt"]);
                        if (lookAtObj != null)
                        {
                            vcam.LookAt = lookAtObj.transform;
                        }
                        else
                        {
                            return $"Error: LookAt target '{parameters["lookAt"]}' not found";
                        }
                    }
                }

                EditorUtility.SetDirty(vcam);

                return $"Camera '{parameters["camera"]}' targets updated";
            }
            catch (Exception e)
            {
                return $"Error: Failed to update camera target - {e.Message}";
            }
#elif CINEMACHINE_3
            try
            {
                if (!parameters.ContainsKey("camera"))
                {
                    return "Error: 'camera' parameter is required";
                }

                var cameraObj = GameObject.Find(parameters["camera"]);
                if (cameraObj == null)
                {
                    return $"Error: Camera '{parameters["camera"]}' not found";
                }

                // CM3 uses CinemachineCamera instead of base class for targets
                var vcam = cameraObj.GetComponent<CinemachineCamera>();
                if (vcam == null)
                {
                    // Fallback to base class
                    var vcamBase = cameraObj.GetComponent<CinemachineVirtualCameraBase>();
                    if (vcamBase != null)
                    {
                        // For non-CinemachineCamera types (rare in CM3)
                        if (parameters.ContainsKey("follow"))
                        {
                            if (string.IsNullOrEmpty(parameters["follow"]))
                            {
                                vcamBase.Follow = null;
                            }
                            else
                            {
                                var followObj = GameObject.Find(parameters["follow"]);
                                if (followObj != null)
                                    vcamBase.Follow = followObj.transform;
                                else
                                    return $"Error: Follow target '{parameters["follow"]}' not found";
                            }
                        }

                        if (parameters.ContainsKey("lookAt"))
                        {
                            if (string.IsNullOrEmpty(parameters["lookAt"]))
                            {
                                vcamBase.LookAt = null;
                            }
                            else
                            {
                                var lookAtObj = GameObject.Find(parameters["lookAt"]);
                                if (lookAtObj != null)
                                    vcamBase.LookAt = lookAtObj.transform;
                                else
                                    return $"Error: LookAt target '{parameters["lookAt"]}' not found";
                            }
                        }

                        EditorUtility.SetDirty(vcamBase);
                        return $"Camera '{parameters["camera"]}' targets updated";
                    }

                    return $"Error: '{parameters["camera"]}' is not a Cinemachine camera";
                }

                // Follow settings (Target.TrackingTarget in CM3)
                if (parameters.ContainsKey("follow"))
                {
                    if (string.IsNullOrEmpty(parameters["follow"]))
                    {
                        vcam.Target.TrackingTarget = null;
                    }
                    else
                    {
                        var followObj = GameObject.Find(parameters["follow"]);
                        if (followObj != null)
                        {
                            vcam.Target.TrackingTarget = followObj.transform;
                        }
                        else
                        {
                            return $"Error: Follow target '{parameters["follow"]}' not found";
                        }
                    }
                }

                // LookAt settings (Target.LookAtTarget in CM3)
                if (parameters.ContainsKey("lookAt"))
                {
                    if (string.IsNullOrEmpty(parameters["lookAt"]))
                    {
                        vcam.Target.LookAtTarget = null;
                    }
                    else
                    {
                        var lookAtObj = GameObject.Find(parameters["lookAt"]);
                        if (lookAtObj != null)
                        {
                            vcam.Target.LookAtTarget = lookAtObj.transform;
                        }
                        else
                        {
                            return $"Error: LookAt target '{parameters["lookAt"]}' not found";
                        }
                    }
                }

                EditorUtility.SetDirty(vcam);

                return $"Camera '{parameters["camera"]}' targets updated";
            }
            catch (Exception e)
            {
                return $"Error: Failed to update camera target - {e.Message}";
            }
#else
            return "Error: Cinemachine package not installed. Please install via Package Manager.";
#endif
        }

        /// <summary>
        /// Update Cinemachine Brain blend settings
        /// </summary>
        public static string UpdateBrainBlendSettings(Dictionary<string, string> parameters)
        {
#if CINEMACHINE_2
            try
            {
                // Get Main Camera
                var mainCamera = Camera.main;
                if (mainCamera == null)
                {
                    return "Error: Main Camera not found";
                }

                var brain = mainCamera.GetComponent<CinemachineBrain>();
                if (brain == null)
                {
                    return "Error: CinemachineBrain not found on Main Camera";
                }

                // Default Blend settings
                if (parameters.ContainsKey("defaultBlendTime"))
                {
                    brain.m_DefaultBlend.m_Time = float.Parse(parameters["defaultBlendTime"]);
                }

                if (parameters.ContainsKey("defaultBlendStyle"))
                {
                    brain.m_DefaultBlend.m_Style = ParseBlendStyle(parameters["defaultBlendStyle"]);
                }

                // Custom Blends settings (requires asset)
                if (parameters.ContainsKey("customBlendsAsset"))
                {
                    var asset = AssetDatabase.LoadAssetAtPath<CinemachineBlenderSettings>(parameters["customBlendsAsset"]);
                    if (asset != null)
                    {
                        brain.m_CustomBlends = asset;
                    }
                }

                EditorUtility.SetDirty(brain);

                return "Cinemachine Brain blend settings updated";
            }
            catch (Exception e)
            {
                return $"Error: Failed to update Brain blend settings - {e.Message}";
            }
#elif CINEMACHINE_3
            try
            {
                // Get Main Camera
                var mainCamera = Camera.main;
                if (mainCamera == null)
                {
                    return "Error: Main Camera not found";
                }

                var brain = mainCamera.GetComponent<CinemachineBrain>();
                if (brain == null)
                {
                    return "Error: CinemachineBrain not found on Main Camera";
                }

                // Default Blend settings (no m_ prefix in CM3)
                if (parameters.ContainsKey("defaultBlendTime"))
                {
                    brain.DefaultBlend.Time = float.Parse(parameters["defaultBlendTime"]);
                }

                if (parameters.ContainsKey("defaultBlendStyle"))
                {
                    brain.DefaultBlend.Style = ParseBlendStyle3(parameters["defaultBlendStyle"]);
                }

                // Custom Blends settings
                if (parameters.ContainsKey("customBlendsAsset"))
                {
                    var asset = AssetDatabase.LoadAssetAtPath<CinemachineBlenderSettings>(parameters["customBlendsAsset"]);
                    if (asset != null)
                    {
                        brain.CustomBlends = asset;
                    }
                }

                EditorUtility.SetDirty(brain);

                return "Cinemachine Brain blend settings updated";
            }
            catch (Exception e)
            {
                return $"Error: Failed to update Brain blend settings - {e.Message}";
            }
#else
            return "Error: Cinemachine package not installed. Please install via Package Manager.";
#endif
        }

        /// <summary>
        /// Get active Cinemachine Camera information
        /// </summary>
        public static string GetActiveCameraInfo(Dictionary<string, string> parameters)
        {
#if CINEMACHINE_2
            try
            {
                var mainCamera = Camera.main;
                if (mainCamera == null)
                {
                    return "Error: Main Camera not found";
                }

                var brain = mainCamera.GetComponent<CinemachineBrain>();
                if (brain == null)
                {
                    return "Error: CinemachineBrain not found on Main Camera";
                }

                var activeVcam = brain.ActiveVirtualCamera;
                if (activeVcam == null)
                {
                    return "No active Cinemachine camera";
                }

                var info = new System.Text.StringBuilder();
                info.AppendLine($"Active Camera: {activeVcam.Name}");
                info.AppendLine($"State: {activeVcam.State}");

                // CM2: ICinemachineCamera has Priority/Follow/LookAt directly
                info.AppendLine($"Priority: {activeVcam.Priority}");

                if (activeVcam.Follow != null)
                {
                    info.AppendLine($"Follow: {activeVcam.Follow.name}");
                }

                if (activeVcam.LookAt != null)
                {
                    info.AppendLine($"LookAt: {activeVcam.LookAt.name}");
                }

                return info.ToString();
            }
            catch (Exception e)
            {
                return $"Error: Failed to get active camera info - {e.Message}";
            }
#elif CINEMACHINE_3
            try
            {
                var mainCamera = Camera.main;
                if (mainCamera == null)
                {
                    return "Error: Main Camera not found";
                }

                var brain = mainCamera.GetComponent<CinemachineBrain>();
                if (brain == null)
                {
                    return "Error: CinemachineBrain not found on Main Camera";
                }

                var activeVcam = brain.ActiveVirtualCamera;
                if (activeVcam == null)
                {
                    return "No active Cinemachine camera";
                }

                var info = new System.Text.StringBuilder();
                info.AppendLine($"Active Camera: {activeVcam.Name}");
                info.AppendLine($"State: {activeVcam.State}");

                // CM3: ICinemachineCamera doesn't have Priority/Follow/LookAt
                // Need to cast to CinemachineCamera to access these properties
                if (activeVcam is CinemachineCamera vcam)
                {
                    info.AppendLine($"Priority: {vcam.Priority}");

                    if (vcam.Target.TrackingTarget != null)
                    {
                        info.AppendLine($"Follow: {vcam.Target.TrackingTarget.name}");
                    }

                    if (vcam.Target.LookAtTarget != null)
                    {
                        info.AppendLine($"LookAt: {vcam.Target.LookAtTarget.name}");
                    }
                }

                return info.ToString();
            }
            catch (Exception e)
            {
                return $"Error: Failed to get active camera info - {e.Message}";
            }
#else
            return "Error: Cinemachine package not installed. Please install via Package Manager.";
#endif
        }

        /// <summary>
        /// Add Collider Extension to Virtual Camera
        /// </summary>
        public static string AddColliderExtension(Dictionary<string, string> parameters)
        {
#if CINEMACHINE_2
            try
            {
                if (!parameters.ContainsKey("camera"))
                {
                    return "Error: 'camera' parameter is required";
                }

                var vcamObj = GameObject.Find(parameters["camera"]);
                if (vcamObj == null)
                {
                    return $"Error: Virtual Camera '{parameters["camera"]}' not found";
                }

                var vcam = vcamObj.GetComponent<CinemachineVirtualCamera>();
                if (vcam == null)
                {
                    return $"Error: '{parameters["camera"]}' is not a Virtual Camera";
                }

                // Add Collider Extension
                var collider = vcam.gameObject.AddComponent<CinemachineCollider>();

                // Settings
                if (parameters.ContainsKey("minimumDistanceFromTarget"))
                {
                    collider.m_MinimumDistanceFromTarget = float.Parse(parameters["minimumDistanceFromTarget"]);
                }

                if (parameters.ContainsKey("avoidObstacles"))
                {
                    collider.m_AvoidObstacles = bool.Parse(parameters["avoidObstacles"]);
                }

                if (parameters.ContainsKey("distanceLimit"))
                {
                    collider.m_DistanceLimit = float.Parse(parameters["distanceLimit"]);
                }

                if (parameters.ContainsKey("smoothingTime"))
                {
                    collider.m_SmoothingTime = float.Parse(parameters["smoothingTime"]);
                }

                if (parameters.ContainsKey("damping"))
                {
                    collider.m_Damping = float.Parse(parameters["damping"]);
                }

                EditorUtility.SetDirty(vcam);

                return $"Collider Extension added to '{parameters["camera"]}'";
            }
            catch (Exception e)
            {
                return $"Error: Failed to add Collider Extension - {e.Message}";
            }
#elif CINEMACHINE_3
            try
            {
                if (!parameters.ContainsKey("camera"))
                {
                    return "Error: 'camera' parameter is required";
                }

                var vcamObj = GameObject.Find(parameters["camera"]);
                if (vcamObj == null)
                {
                    return $"Error: Virtual Camera '{parameters["camera"]}' not found";
                }

                var vcam = vcamObj.GetComponent<CinemachineCamera>();
                if (vcam == null)
                {
                    return $"Error: '{parameters["camera"]}' is not a Cinemachine Camera";
                }

                // Add Deoccluder extension (CM3 equivalent of Collider)
                var deoccluder = vcam.gameObject.AddComponent<CinemachineDeoccluder>();

                if (parameters.ContainsKey("minimumDistanceFromTarget"))
                {
                    deoccluder.MinimumDistanceFromTarget = float.Parse(parameters["minimumDistanceFromTarget"]);
                }

                if (parameters.ContainsKey("smoothingTime"))
                {
                    // CM3: Damping is now inside AvoidObstacles struct
                    var avoidObstacles = deoccluder.AvoidObstacles;
                    avoidObstacles.Damping = float.Parse(parameters["smoothingTime"]);
                    deoccluder.AvoidObstacles = avoidObstacles;
                }

                EditorUtility.SetDirty(vcam);

                return $"Deoccluder Extension (CM3 Collider) added to '{parameters["camera"]}'";
            }
            catch (Exception e)
            {
                return $"Error: Failed to add Collider Extension - {e.Message}";
            }
#else
            return "Error: Cinemachine package not installed. Please install via Package Manager.";
#endif
        }

        /// <summary>
        /// Add Confiner Extension to Virtual Camera
        /// </summary>
        public static string AddConfinerExtension(Dictionary<string, string> parameters)
        {
#if CINEMACHINE_2
            try
            {
                if (!parameters.ContainsKey("camera"))
                {
                    return "Error: 'camera' parameter is required";
                }

                var vcamObj = GameObject.Find(parameters["camera"]);
                if (vcamObj == null)
                {
                    return $"Error: Virtual Camera '{parameters["camera"]}' not found";
                }

                var vcam = vcamObj.GetComponent<CinemachineVirtualCamera>();
                if (vcam == null)
                {
                    return $"Error: '{parameters["camera"]}' is not a Virtual Camera";
                }

                // Add Confiner Extension
                var confiner = vcam.gameObject.AddComponent<CinemachineConfiner>();

                // Bounding Volume settings
                if (parameters.ContainsKey("boundingVolume"))
                {
                    var volumeObj = GameObject.Find(parameters["boundingVolume"]);
                    if (volumeObj != null)
                    {
                        var collider = volumeObj.GetComponent<Collider>();
                        if (collider != null)
                        {
                            confiner.m_BoundingVolume = collider;
                        }
                    }
                }

                // Confine Mode settings
                if (parameters.ContainsKey("confineMode"))
                {
                    string mode = parameters["confineMode"];
                    confiner.m_ConfineMode = mode == "Confine3D" ?
                        CinemachineConfiner.Mode.Confine3D :
                        CinemachineConfiner.Mode.Confine2D;
                }

                // Damping settings
                if (parameters.ContainsKey("damping"))
                {
                    confiner.m_Damping = float.Parse(parameters["damping"]);
                }

                EditorUtility.SetDirty(vcam);

                return $"Confiner Extension added to '{parameters["camera"]}'";
            }
            catch (Exception e)
            {
                return $"Error: Failed to add Confiner Extension - {e.Message}";
            }
#elif CINEMACHINE_3
            try
            {
                if (!parameters.ContainsKey("camera"))
                {
                    return "Error: 'camera' parameter is required";
                }

                var vcamObj = GameObject.Find(parameters["camera"]);
                if (vcamObj == null)
                {
                    return $"Error: Virtual Camera '{parameters["camera"]}' not found";
                }

                var vcam = vcamObj.GetComponent<CinemachineCamera>();
                if (vcam == null)
                {
                    return $"Error: '{parameters["camera"]}' is not a Cinemachine Camera";
                }

                // Add Confiner3D extension (CM3 version)
                var confiner = vcam.gameObject.AddComponent<CinemachineConfiner3D>();

                if (parameters.ContainsKey("boundingVolume"))
                {
                    var volumeObj = GameObject.Find(parameters["boundingVolume"]);
                    if (volumeObj != null)
                    {
                        var collider = volumeObj.GetComponent<Collider>();
                        if (collider != null)
                        {
                            confiner.BoundingVolume = collider;
                        }
                    }
                }

                EditorUtility.SetDirty(vcam);

                return $"Confiner3D Extension added to '{parameters["camera"]}'";
            }
            catch (Exception e)
            {
                return $"Error: Failed to add Confiner Extension - {e.Message}";
            }
#else
            return "Error: Cinemachine package not installed. Please install via Package Manager.";
#endif
        }

        /// <summary>
        /// Create Dolly Track
        /// </summary>
        public static string CreateDollyTrack(Dictionary<string, string> parameters)
        {
#if CINEMACHINE_2
            try
            {
                string name = parameters.ContainsKey("name") ? parameters["name"] : "DollyTrack";

                // Create Path object
                GameObject pathObj = new GameObject(name);
                var path = pathObj.AddComponent<CinemachineSmoothPath>();

                // Waypoints settings
                if (parameters.ContainsKey("waypoints"))
                {
                    var waypointsJson = parameters["waypoints"];
                    var waypoints = Newtonsoft.Json.JsonConvert.DeserializeObject<List<Dictionary<string, float>>>(waypointsJson);

                    path.m_Waypoints = new CinemachineSmoothPath.Waypoint[waypoints.Count];
                    for (int i = 0; i < waypoints.Count; i++)
                    {
                        var wp = waypoints[i];
                        path.m_Waypoints[i] = new CinemachineSmoothPath.Waypoint
                        {
                            position = new Vector3(wp["x"], wp["y"], wp["z"])
                        };
                    }
                }

                // Loop settings
                if (parameters.ContainsKey("looped"))
                {
                    path.m_Looped = bool.Parse(parameters["looped"]);
                }

                // Connect to Virtual Camera
                if (parameters.ContainsKey("cameraName"))
                {
                    var vcamObj = GameObject.Find(parameters["cameraName"]);
                    if (vcamObj != null)
                    {
                        var vcam = vcamObj.GetComponent<CinemachineVirtualCamera>();
                        if (vcam != null)
                        {
                            var dolly = vcam.AddCinemachineComponent<CinemachineTrackedDolly>();
                            dolly.m_Path = path;
                        }
                    }
                }

                Undo.RegisterCreatedObjectUndo(pathObj, "Create Dolly Track");
                Selection.activeGameObject = pathObj;

                return $"Dolly Track '{name}' created with {path.m_Waypoints.Length} waypoints";
            }
            catch (Exception e)
            {
                return $"Error: Failed to create Dolly Track - {e.Message}";
            }
#elif CINEMACHINE_3
            try
            {
                string name = parameters.ContainsKey("name") ? parameters["name"] : "DollyTrack";

                // In CM3, use Unity Splines instead of CinemachineSmoothPath
                GameObject pathObj = new GameObject(name);
                var splineContainer = pathObj.AddComponent<SplineContainer>();

                // Basic spline setup (CM3 uses Unity's Spline system)
                // Note: Full waypoint configuration requires Unity Splines API
                if (parameters.ContainsKey("looped") && bool.Parse(parameters["looped"]))
                {
                    splineContainer.Spline.Closed = true;
                }

                // Connect to Virtual Camera (if specified)
                if (parameters.ContainsKey("cameraName"))
                {
                    var vcamObj = GameObject.Find(parameters["cameraName"]);
                    if (vcamObj != null)
                    {
                        var vcam = vcamObj.GetComponent<CinemachineCamera>();
                        if (vcam != null)
                        {
                            // Add SplineCart or SplineDolly component
                            var splineDolly = vcam.gameObject.AddComponent<CinemachineSplineDolly>();
                            splineDolly.Spline = splineContainer;
                        }
                    }
                }

                Undo.RegisterCreatedObjectUndo(pathObj, "Create Dolly Track");
                Selection.activeGameObject = pathObj;

                return $"Dolly Track '{name}' created (CM3 uses Unity Splines - configure waypoints in Inspector)";
            }
            catch (Exception e)
            {
                return $"Error: Failed to create Dolly Track - {e.Message}";
            }
#else
            return "Error: Cinemachine package not installed. Please install via Package Manager.";
#endif
        }

        // Helper methods
        private static Vector3 ParseVector3(string vectorString)
        {
            var parts = vectorString.Split(',');
            return new Vector3(
                float.Parse(parts[0]),
                float.Parse(parts[1]),
                float.Parse(parts[2])
            );
        }

#if CINEMACHINE_2
        /// <summary>
        /// Add Body component
        /// </summary>
        private static void AddBodyComponent(CinemachineVirtualCamera vcam, string bodyType, Dictionary<string, string> parameters)
        {
            switch (bodyType)
            {
                case "Transposer":
                    var transposer = vcam.AddCinemachineComponent<CinemachineTransposer>();
                    if (parameters.ContainsKey("damping"))
                    {
                        var damping = ParseVector3(parameters["damping"]);
                        transposer.m_XDamping = damping.x;
                        transposer.m_YDamping = damping.y;
                        transposer.m_ZDamping = damping.z;
                    }
                    if (parameters.ContainsKey("offset"))
                    {
                        transposer.m_FollowOffset = ParseVector3(parameters["offset"]);
                    }
                    break;

                case "FramingTransposer":
                    var framingTransposer = vcam.AddCinemachineComponent<CinemachineFramingTransposer>();
                    if (parameters.ContainsKey("damping"))
                    {
                        var damping = ParseVector3(parameters["damping"]);
                        framingTransposer.m_XDamping = damping.x;
                        framingTransposer.m_YDamping = damping.y;
                        framingTransposer.m_ZDamping = damping.z;
                    }
                    if (parameters.ContainsKey("screenX"))
                        framingTransposer.m_ScreenX = float.Parse(parameters["screenX"]);
                    if (parameters.ContainsKey("screenY"))
                        framingTransposer.m_ScreenY = float.Parse(parameters["screenY"]);
                    break;

                case "OrbitalTransposer":
                    var orbital = vcam.AddCinemachineComponent<CinemachineOrbitalTransposer>();
                    if (parameters.ContainsKey("offset"))
                    {
                        orbital.m_FollowOffset = ParseVector3(parameters["offset"]);
                    }
                    break;

                case "HardLockToTarget":
                    vcam.AddCinemachineComponent<CinemachineHardLockToTarget>();
                    break;

                case "DoNothing":
                    // No Body component
                    break;

                default:
                    // Default is Transposer
                    vcam.AddCinemachineComponent<CinemachineTransposer>();
                    break;
            }
        }

        /// <summary>
        /// Add Aim component
        /// </summary>
        private static void AddAimComponent(CinemachineVirtualCamera vcam, string aimType, Dictionary<string, string> parameters)
        {
            switch (aimType)
            {
                case "Composer":
                    var composer = vcam.AddCinemachineComponent<CinemachineComposer>();
                    if (parameters.ContainsKey("screenX"))
                        composer.m_ScreenX = float.Parse(parameters["screenX"]);
                    if (parameters.ContainsKey("screenY"))
                        composer.m_ScreenY = float.Parse(parameters["screenY"]);
                    if (parameters.ContainsKey("deadZoneWidth"))
                        composer.m_DeadZoneWidth = float.Parse(parameters["deadZoneWidth"]);
                    if (parameters.ContainsKey("deadZoneHeight"))
                        composer.m_DeadZoneHeight = float.Parse(parameters["deadZoneHeight"]);
                    break;

                case "GroupComposer":
                    var groupComposer = vcam.AddCinemachineComponent<CinemachineGroupComposer>();
                    if (parameters.ContainsKey("screenX"))
                        groupComposer.m_ScreenX = float.Parse(parameters["screenX"]);
                    if (parameters.ContainsKey("screenY"))
                        groupComposer.m_ScreenY = float.Parse(parameters["screenY"]);
                    break;

                case "POV":
                    var pov = vcam.AddCinemachineComponent<CinemachinePOV>();
                    if (parameters.ContainsKey("horizontalAxis"))
                    {
                        pov.m_HorizontalAxis.m_MaxSpeed = float.Parse(parameters["horizontalAxis"]);
                    }
                    if (parameters.ContainsKey("verticalAxis"))
                    {
                        pov.m_VerticalAxis.m_MaxSpeed = float.Parse(parameters["verticalAxis"]);
                    }
                    break;

                case "SameAsFollowTarget":
                    vcam.AddCinemachineComponent<CinemachineSameAsFollowTarget>();
                    break;

                case "HardLookAt":
                    vcam.AddCinemachineComponent<CinemachineHardLookAt>();
                    break;

                case "DoNothing":
                    // No Aim component
                    break;

                default:
                    // Default is Composer
                    vcam.AddCinemachineComponent<CinemachineComposer>();
                    break;
            }
        }

        /// <summary>
        /// Add Noise component (camera shake)
        /// </summary>
        private static void AddNoiseComponent(CinemachineVirtualCamera vcam, Dictionary<string, string> parameters)
        {
            var noise = vcam.AddCinemachineComponent<CinemachineBasicMultiChannelPerlin>();

            // Profile settings (preset)
            if (parameters.ContainsKey("noiseProfile"))
            {
                string profileName = parameters["noiseProfile"];
                NoiseSettings profile = GetNoiseProfile(profileName);
                if (profile != null)
                {
                    noise.m_NoiseProfile = profile;
                }
            }

            // Amplitude settings
            if (parameters.ContainsKey("noiseAmplitude"))
            {
                noise.m_AmplitudeGain = float.Parse(parameters["noiseAmplitude"]);
            }

            // Frequency settings
            if (parameters.ContainsKey("noiseFrequency"))
            {
                noise.m_FrequencyGain = float.Parse(parameters["noiseFrequency"]);
            }
        }

        /// <summary>
        /// Get Noise profile
        /// </summary>
        private static NoiseSettings GetNoiseProfile(string profileName)
        {
            // Search for built-in profiles
            string[] guids = AssetDatabase.FindAssets("t:NoiseSettings");
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.Contains(profileName) || System.IO.Path.GetFileNameWithoutExtension(path) == profileName)
                {
                    return AssetDatabase.LoadAssetAtPath<NoiseSettings>(path);
                }
            }

            // Default if not found (6D Shake)
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.Contains("6D Shake"))
                {
                    return AssetDatabase.LoadAssetAtPath<NoiseSettings>(path);
                }
            }

            return null;
        }

        private static CinemachineBlendDefinition.Style ParseBlendStyle(string style)
        {
            switch (style)
            {
                case "Cut": return CinemachineBlendDefinition.Style.Cut;
                case "EaseInOut": return CinemachineBlendDefinition.Style.EaseInOut;
                case "EaseIn": return CinemachineBlendDefinition.Style.EaseIn;
                case "EaseOut": return CinemachineBlendDefinition.Style.EaseOut;
                case "HardIn": return CinemachineBlendDefinition.Style.HardIn;
                case "HardOut": return CinemachineBlendDefinition.Style.HardOut;
                case "Linear": return CinemachineBlendDefinition.Style.Linear;
                default: return CinemachineBlendDefinition.Style.EaseInOut;
            }
        }

        private static CinemachineBrain.UpdateMethod ParseUpdateMethod(string method)
        {
            switch (method)
            {
                case "FixedUpdate": return CinemachineBrain.UpdateMethod.FixedUpdate;
                case "LateUpdate": return CinemachineBrain.UpdateMethod.LateUpdate;
                case "SmartUpdate": return CinemachineBrain.UpdateMethod.SmartUpdate;
                default: return CinemachineBrain.UpdateMethod.SmartUpdate;
            }
        }

        private static CinemachineTargetGroup.PositionMode ParseTargetGroupPositionMode(string mode)
        {
            switch (mode)
            {
                case "GroupCenter": return CinemachineTargetGroup.PositionMode.GroupCenter;
                case "GroupAverage": return CinemachineTargetGroup.PositionMode.GroupAverage;
                default: return CinemachineTargetGroup.PositionMode.GroupCenter;
            }
        }

        private static CinemachineTargetGroup.RotationMode ParseTargetGroupRotationMode(string mode)
        {
            switch (mode)
            {
                case "Manual": return CinemachineTargetGroup.RotationMode.Manual;
                case "GroupAverage": return CinemachineTargetGroup.RotationMode.GroupAverage;
                default: return CinemachineTargetGroup.RotationMode.Manual;
            }
        }

        private static CinemachineTargetGroup.UpdateMethod ParseTargetGroupUpdateMethod(string method)
        {
            switch (method)
            {
                case "Update": return CinemachineTargetGroup.UpdateMethod.Update;
                case "FixedUpdate": return CinemachineTargetGroup.UpdateMethod.FixedUpdate;
                case "LateUpdate": return CinemachineTargetGroup.UpdateMethod.LateUpdate;
                default: return CinemachineTargetGroup.UpdateMethod.Update;
            }
        }
#elif CINEMACHINE_3
        // Cinemachine 3.x Helper Methods

        /// <summary>
        /// Add Body component for Cinemachine 3.x
        /// </summary>
        private static void AddBodyComponent3(CinemachineCamera vcam, string bodyType, Dictionary<string, string> parameters)
        {
            switch (bodyType)
            {
                case "Follow":
                case "Transposer":
                    var follow = vcam.gameObject.AddComponent<CinemachineFollow>();
                    if (parameters.ContainsKey("damping"))
                    {
                        var damping = ParseVector3(parameters["damping"]);
                        // CM3: Damping moved to TrackerSettings.PositionDamping
                        var trackerSettings = follow.TrackerSettings;
                        trackerSettings.PositionDamping = damping;
                        follow.TrackerSettings = trackerSettings;
                    }
                    if (parameters.ContainsKey("offset"))
                    {
                        follow.FollowOffset = ParseVector3(parameters["offset"]);
                    }
                    break;

                case "PositionComposer":
                case "FramingTransposer":
                    var positionComposer = vcam.gameObject.AddComponent<CinemachinePositionComposer>();
                    if (parameters.ContainsKey("damping"))
                    {
                        var damping = ParseVector3(parameters["damping"]);
                        positionComposer.Damping = damping;
                    }
                    if (parameters.ContainsKey("screenX") || parameters.ContainsKey("screenY"))
                    {
                        var composition = positionComposer.Composition;
                        var screenPos = composition.ScreenPosition;
                        if (parameters.ContainsKey("screenX"))
                            screenPos.x = float.Parse(parameters["screenX"]);
                        if (parameters.ContainsKey("screenY"))
                            screenPos.y = float.Parse(parameters["screenY"]);
                        composition.ScreenPosition = screenPos;
                        positionComposer.Composition = composition;
                    }
                    break;

                case "OrbitalFollow":
                case "OrbitalTransposer":
                    var orbital = vcam.gameObject.AddComponent<CinemachineOrbitalFollow>();
                    if (parameters.ContainsKey("offset"))
                    {
                        orbital.TargetOffset = ParseVector3(parameters["offset"]);
                    }
                    break;

                case "HardLockToTarget":
                    // In CM3, this is built into CinemachineCamera's tracking
                    // No separate component needed
                    break;

                case "DoNothing":
                    // No Body component
                    break;

                default:
                    // Default is Follow
                    vcam.gameObject.AddComponent<CinemachineFollow>();
                    break;
            }
        }

        /// <summary>
        /// Add Aim component for Cinemachine 3.x
        /// </summary>
        private static void AddAimComponent3(CinemachineCamera vcam, string aimType, Dictionary<string, string> parameters)
        {
            switch (aimType)
            {
                case "Composer":
                case "RotationComposer":
                    var composer = vcam.gameObject.AddComponent<CinemachineRotationComposer>();
                    if (parameters.ContainsKey("screenX") || parameters.ContainsKey("screenY") ||
                        parameters.ContainsKey("deadZoneWidth") || parameters.ContainsKey("deadZoneHeight"))
                    {
                        var composition = composer.Composition;

                        if (parameters.ContainsKey("screenX") || parameters.ContainsKey("screenY"))
                        {
                            var screenPos = composition.ScreenPosition;
                            if (parameters.ContainsKey("screenX"))
                                screenPos.x = float.Parse(parameters["screenX"]);
                            if (parameters.ContainsKey("screenY"))
                                screenPos.y = float.Parse(parameters["screenY"]);
                            composition.ScreenPosition = screenPos;
                        }

                        if (parameters.ContainsKey("deadZoneWidth") || parameters.ContainsKey("deadZoneHeight"))
                        {
                            var deadZone = composition.DeadZone;
                            Vector2 size = deadZone.Size;
                            if (parameters.ContainsKey("deadZoneWidth"))
                                size.x = float.Parse(parameters["deadZoneWidth"]);
                            if (parameters.ContainsKey("deadZoneHeight"))
                                size.y = float.Parse(parameters["deadZoneHeight"]);
                            deadZone.Size = size;
                            composition.DeadZone = deadZone;
                        }

                        composer.Composition = composition;
                    }
                    break;

                case "GroupComposer":
                    // In CM3, use RotationComposer with target group
                    var groupComposer = vcam.gameObject.AddComponent<CinemachineRotationComposer>();
                    if (parameters.ContainsKey("screenX") || parameters.ContainsKey("screenY"))
                    {
                        var composition = groupComposer.Composition;
                        var screenPos = composition.ScreenPosition;
                        if (parameters.ContainsKey("screenX"))
                            screenPos.x = float.Parse(parameters["screenX"]);
                        if (parameters.ContainsKey("screenY"))
                            screenPos.y = float.Parse(parameters["screenY"]);
                        composition.ScreenPosition = screenPos;
                        groupComposer.Composition = composition;
                    }
                    break;

                case "POV":
                    // POV in CM3 uses InputAxisController
                    var inputAxis = vcam.gameObject.AddComponent<CinemachineInputAxisController>();
                    // Note: CM3's InputAxisController configuration is primarily done via Inspector
                    // Dynamic MaxSpeed modification is not supported in the same way as CM2
                    if (parameters.ContainsKey("horizontalAxis") || parameters.ContainsKey("verticalAxis"))
                    {
                        SynLog.Warn("[Synaptic] CM3: POV axis speed configuration not supported via script. Configure in Inspector.");
                    }
                    break;

                case "SameAsFollowTarget":
                    // Built into CM3 tracking
                    break;

                case "HardLookAt":
                    // Built into CM3 LookAt target
                    break;

                case "DoNothing":
                    // No Aim component
                    break;

                default:
                    // Default is RotationComposer
                    vcam.gameObject.AddComponent<CinemachineRotationComposer>();
                    break;
            }
        }

        /// <summary>
        /// Add Noise component for Cinemachine 3.x (camera shake)
        /// </summary>
        private static void AddNoiseComponent3(CinemachineCamera vcam, Dictionary<string, string> parameters)
        {
            var noise = vcam.gameObject.AddComponent<CinemachineBasicMultiChannelPerlin>();

            // Profile settings (preset)
            if (parameters.ContainsKey("noiseProfile"))
            {
                string profileName = parameters["noiseProfile"];
                NoiseSettings profile = GetNoiseProfile(parameters["noiseProfile"]);
                if (profile != null)
                {
                    noise.NoiseProfile = profile;
                }
            }

            // Amplitude settings (no m_ prefix in CM3)
            if (parameters.ContainsKey("noiseAmplitude"))
            {
                noise.AmplitudeGain = float.Parse(parameters["noiseAmplitude"]);
            }

            // Frequency settings
            if (parameters.ContainsKey("noiseFrequency"))
            {
                noise.FrequencyGain = float.Parse(parameters["noiseFrequency"]);
            }
        }

        /// <summary>
        /// Get Noise profile (CM3)
        /// </summary>
        private static NoiseSettings GetNoiseProfile(string profileName)
        {
            // Search for built-in profiles
            string[] guids = AssetDatabase.FindAssets("t:NoiseSettings");
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.Contains(profileName) || System.IO.Path.GetFileNameWithoutExtension(path) == profileName)
                {
                    return AssetDatabase.LoadAssetAtPath<NoiseSettings>(path);
                }
            }

            // Default if not found (6D Shake)
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.Contains("6D Shake"))
                {
                    return AssetDatabase.LoadAssetAtPath<NoiseSettings>(path);
                }
            }

            return null;
        }

        private static CinemachineBlendDefinition.Styles ParseBlendStyle3(string style)
        {
            switch (style)
            {
                case "Cut": return CinemachineBlendDefinition.Styles.Cut;
                case "EaseInOut": return CinemachineBlendDefinition.Styles.EaseInOut;
                case "EaseIn": return CinemachineBlendDefinition.Styles.EaseIn;
                case "EaseOut": return CinemachineBlendDefinition.Styles.EaseOut;
                case "HardIn": return CinemachineBlendDefinition.Styles.HardIn;
                case "HardOut": return CinemachineBlendDefinition.Styles.HardOut;
                case "Linear": return CinemachineBlendDefinition.Styles.Linear;
                default: return CinemachineBlendDefinition.Styles.EaseInOut;
            }
        }

        private static CinemachineBrain.UpdateMethods ParseUpdateMethod3(string method)
        {
            switch (method)
            {
                case "FixedUpdate": return CinemachineBrain.UpdateMethods.FixedUpdate;
                case "LateUpdate": return CinemachineBrain.UpdateMethods.LateUpdate;
                case "SmartUpdate": return CinemachineBrain.UpdateMethods.SmartUpdate;
                default: return CinemachineBrain.UpdateMethods.SmartUpdate;
            }
        }

        private static CinemachineTargetGroup.PositionModes ParseTargetGroupPositionMode3(string mode)
        {
            switch (mode)
            {
                case "GroupCenter": return CinemachineTargetGroup.PositionModes.GroupCenter;
                case "GroupAverage": return CinemachineTargetGroup.PositionModes.GroupAverage;
                default: return CinemachineTargetGroup.PositionModes.GroupCenter;
            }
        }

        private static CinemachineTargetGroup.RotationModes ParseTargetGroupRotationMode3(string mode)
        {
            switch (mode)
            {
                case "Manual": return CinemachineTargetGroup.RotationModes.Manual;
                case "GroupAverage": return CinemachineTargetGroup.RotationModes.GroupAverage;
                default: return CinemachineTargetGroup.RotationModes.Manual;
            }
        }

        private static CinemachineTargetGroup.UpdateMethods ParseTargetGroupUpdateMethod3(string method)
        {
            switch (method)
            {
                case "Update": return CinemachineTargetGroup.UpdateMethods.Update;
                case "FixedUpdate": return CinemachineTargetGroup.UpdateMethods.FixedUpdate;
                case "LateUpdate": return CinemachineTargetGroup.UpdateMethods.LateUpdate;
                default: return CinemachineTargetGroup.UpdateMethods.Update;
            }
        }
#endif
    }
}
