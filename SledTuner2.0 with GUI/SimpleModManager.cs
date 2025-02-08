using UnityEngine;
using UnityEngine.SceneManagement;
using MelonLoader;

namespace SledTunerProject
{
    /// <summary>
    /// Encapsulates the functionality from the original simple mod menu.
    /// This manager handles parameters such as fly mode, physics, engine power, headlight color, etc.
    /// </summary>
    public class SimpleModManager
    {
        // Parameters (copied from the original TestMod)
        public float gravity = -9.81f;
        public float power = 143000f;
        public float lugHeight = 0.18f;
        public float trackLength = 1f;
        public float pitchFactor = 7f;
        public bool isFlying = false;
        public bool driverRagdoll = true; // (formerly notdriverInvincible)
        public bool test = false;
        public float speed = 10f;
        public float rotationSpeed = 400f;
        public bool apply = false;

        // Originals for reset functionality
        public float originalPower;
        public float originalLugHeight;
        public float originalTrackLength;
        public float originalGravity = -9.81f;
        public float originalPitchFactor;

        // Headlight color values
        public float lightR = 1f;
        public float lightG = 1f;
        public float lightB = 1f;

        public bool hasOriginalValues = false;
        public bool valuesReset = false;

        // Cached GameObjects
        public GameObject parentObject = null;
        public GameObject trackRenderer = null;

        /// <summary>
        /// Draws the simple mod menu page.
        /// This replicates the original simple mod menu GUI layout.
        /// </summary>
        public void DrawTestModPage()
        {
            // Use fixed dimensions for the simple mod menu page.
            int boxWidth = 300;
            int boxHeight = 500;
            int centerX = Screen.width / 2;
            int centerY = Screen.height / 2;

            // Draw the background box.
            GUI.Box(new Rect(centerX - boxWidth / 2, centerY - boxHeight / 2, boxWidth, boxHeight), "Simple Mod Menu");

            // Define a starting offset inside the box.
            float offsetX = centerX - 100;
            float offsetY = centerY - boxHeight / 2 + 30;

            // "Apply" toggle and "Reset" button.
            apply = GUI.Toggle(new Rect(offsetX, offsetY, 80, 20), apply, "Apply");
            if (GUI.Button(new Rect(offsetX + 120, offsetY, 80, 20), "Reset"))
            {
                valuesReset = true;
            }
            offsetY += 30;

            // FlySpeed slider and text field.
            GUI.Label(new Rect(offsetX, offsetY, 100, 40), "FlySpeed");
            string speedStr = GUI.TextField(new Rect(offsetX + 140, offsetY + 10, 80, 20), speed.ToString());
            float.TryParse(speedStr, out speed);
            speed = GUI.HorizontalSlider(new Rect(offsetX, offsetY + 30, 220, 20), speed, 0f, 200f);
            offsetY += 50;

            // Gravity slider and text field.
            GUI.Label(new Rect(offsetX, offsetY, 100, 40), "Gravity");
            string gravityStr = GUI.TextField(new Rect(offsetX + 140, offsetY + 10, 80, 20), gravity.ToString());
            float.TryParse(gravityStr, out gravity);
            gravity = GUI.HorizontalSlider(new Rect(offsetX, offsetY + 30, 220, 20), gravity, -10f, 10f);
            offsetY += 50;

            // Power slider and text field.
            GUI.Label(new Rect(offsetX, offsetY, 100, 40), "Power");
            string powerStr = GUI.TextField(new Rect(offsetX + 140, offsetY + 10, 80, 20), power.ToString());
            float.TryParse(powerStr, out power);
            power = GUI.HorizontalSlider(new Rect(offsetX, offsetY + 30, 220, 20), power, 0f, 300000f);
            offsetY += 50;

            // LugHeight slider and text field.
            GUI.Label(new Rect(offsetX, offsetY, 100, 40), "Lugheight");
            string lugHeightStr = GUI.TextField(new Rect(offsetX + 140, offsetY + 10, 80, 20), lugHeight.ToString());
            float.TryParse(lugHeightStr, out lugHeight);
            lugHeight = GUI.HorizontalSlider(new Rect(offsetX, offsetY + 30, 220, 20), lugHeight, 0f, 2f);
            offsetY += 50;

            // TrackLength slider and text field.
            GUI.Label(new Rect(offsetX, offsetY, 100, 40), "TrackLength");
            string trackLengthStr = GUI.TextField(new Rect(offsetX + 140, offsetY + 10, 80, 20), trackLength.ToString());
            float.TryParse(trackLengthStr, out trackLength);
            trackLength = GUI.HorizontalSlider(new Rect(offsetX, offsetY + 30, 220, 20), trackLength, 0.5f, 2f);
            offsetY += 50;

            // PitchFactor slider and text field.
            GUI.Label(new Rect(offsetX, offsetY, 100, 40), "PitchFactor");
            string pitchFactorStr = GUI.TextField(new Rect(offsetX + 140, offsetY + 10, 80, 20), pitchFactor.ToString());
            float.TryParse(pitchFactorStr, out pitchFactor);
            pitchFactor = GUI.HorizontalSlider(new Rect(offsetX, offsetY + 30, 220, 20), pitchFactor, 2f, 30f);
            offsetY += 50;

            // Headlight Color (three channels).
            GUI.Label(new Rect(offsetX, offsetY, 100, 40), "Headlight Color");
            string lightRStr = GUI.TextField(new Rect(offsetX + 140, offsetY + 10, 20, 20), lightR.ToString());
            float.TryParse(lightRStr, out lightR);
            string lightGStr = GUI.TextField(new Rect(offsetX + 170, offsetY + 10, 20, 20), lightG.ToString());
            float.TryParse(lightGStr, out lightG);
            string lightBStr = GUI.TextField(new Rect(offsetX + 200, offsetY + 10, 20, 20), lightB.ToString());
            float.TryParse(lightBStr, out lightB);
            offsetY += 50;

            // Toggles for Driver Ragdoll and "test".
            driverRagdoll = GUI.Toggle(new Rect(offsetX, offsetY, 200, 20), driverRagdoll, "DriverRagdoll");
            test = GUI.Toggle(new Rect(offsetX, offsetY + 30, 200, 20), test, "test");
            offsetY += 70;

            // Footer label.
            GUI.Label(new Rect(centerX + boxWidth / 2 - 200, centerY + boxHeight / 2 - 40, 200, 40), "Made by Samisalami");
        }

        /// <summary>
        /// Contains the update logic for the simple mod.
        /// This method is called from Main.OnUpdate().
        /// </summary>
        public void UpdateTestMod()
        {
            Scene activeScene = SceneManager.GetActiveScene();

            // Toggle flying mode with key J.
            if (Input.GetKeyUp(KeyCode.J))
            {
                isFlying = !isFlying;
            }

            // Reset values if requested.
            if (valuesReset)
            {
                power = originalPower;
                lugHeight = originalLugHeight;
                pitchFactor = originalPitchFactor;
                trackLength = originalTrackLength;
                driverRagdoll = true;
                lightR = 1f;
                lightG = 1f;
                lightB = 1f;
                valuesReset = false;
            }

            // Only run modifications if not in the Garage scene.
            if (activeScene.name != "Garage")
            {
                GameObject bodyGO = GameObject.Find("Snowmobile(Clone)/Body");
                if (bodyGO == null)
                    return;

                GameObject engineSounds = GameObject.Find("Snowmobile(Clone)/Body/EngineSounds");
                GameObject trackGO = GameObject.Find("Snowmobile(Clone)/Body/Track");
                GameObject spotLightGO = GameObject.Find("Snowmobile(Clone)/Body/Spot Light");
                GameObject ragdollTrigger = GameObject.Find("Snowmobile(Clone)/Body/RagdollTerrainTrigger");

                // Get required components.
                Rigidbody bodyRB = bodyGO.GetComponent<Rigidbody>();
                MeshInterpretter meshInterp = bodyGO.GetComponent<MeshInterpretter>();
                Light headLight = spotLightGO != null ? spotLightGO.GetComponent<Light>() : null;

                // Get the RagDollCollisionController from "IK Player (Drivers)" if present.
                RagDollCollisionController ragDollController = null;
                GameObject driversGO = GameObject.Find("Snowmobile(Clone)/Body/IK Player (Drivers)");
                if (driversGO != null)
                {
                    ragDollController = driversGO.GetComponent<RagDollCollisionController>();
                }

                // Cache original values on first run.
                if (!hasOriginalValues)
                {
                    originalLugHeight = meshInterp.lugHeight;
                    originalPower = meshInterp.power;
                    originalTrackLength = trackGO.transform.localScale.z;
                    originalGravity = Physics.gravity.y;
                    originalPitchFactor = meshInterp.pitchFactor;
                    hasOriginalValues = true;

                    // Find the parent object that contains the TrackRenderer.
                    foreach (Transform child in bodyGO.transform)
                    {
                        if (child.Find("TrackRenderer") != null)
                        {
                            parentObject = child.gameObject;
                            break;
                        }
                    }
                    if (parentObject != null)
                    {
                        Transform rendererT = parentObject.transform.Find("TrackRenderer");
                        if (rendererT != null)
                        {
                            trackRenderer = rendererT.gameObject;
                        }
                    }
                }

                // Update track renderer scale if available.
                if (trackRenderer != null)
                {
                    trackRenderer.transform.localScale = new Vector3(trackLength, 1f, 1f);
                }

                // Apply or revert modifications based on the "apply" toggle.
                if (apply)
                {
                    meshInterp.power = power;
                    meshInterp.lugHeight = lugHeight;
                    meshInterp.pitchFactor = pitchFactor;
                    trackGO.transform.localScale = new Vector3(1f, 1f, trackLength);
                    if (headLight != null)
                    {
                        headLight.color = new Color(lightR, lightG, lightB);
                    }
                    if (ragdollTrigger != null)
                    {
                        ragdollTrigger.SetActive(driverRagdoll);
                    }
                    if (ragDollController != null)
                    {
                        ragDollController.enabled = driverRagdoll;
                    }
                    Physics.gravity = new Vector3(0f, gravity, 0f);
                }
                else
                {
                    // Revert to original parameters.
                    meshInterp.power = originalPower;
                    meshInterp.lugHeight = originalLugHeight;
                    meshInterp.pitchFactor = originalPitchFactor;
                    trackGO.transform.localScale = new Vector3(1f, 1f, originalTrackLength);
                    if (headLight != null)
                    {
                        headLight.color = Color.white;
                    }
                    if (ragdollTrigger != null)
                    {
                        ragdollTrigger.SetActive(true);
                    }
                    if (ragDollController != null)
                    {
                        ragDollController.enabled = true;
                    }
                    Physics.gravity = new Vector3(0f, originalGravity, 0f);
                }

                // Handle flying mode.
                if (isFlying)
                {
                    Physics.gravity = Vector3.zero;
                    Vector3 forward = bodyRB.transform.forward;
                    bodyRB.velocity = Vector3.zero;
                    bodyRB.angularVelocity = Vector3.zero;
                    if (Input.GetKey(KeyCode.Space))
                    {
                        bodyRB.MovePosition(bodyRB.position + bodyRB.transform.up * speed * Time.deltaTime);
                    }
                    if (Input.GetKey(KeyCode.LeftShift))
                    {
                        bodyRB.MovePosition(bodyRB.position - bodyRB.transform.up * speed * Time.deltaTime);
                    }
                    if (Input.GetKey(KeyCode.UpArrow))
                    {
                        bodyRB.MovePosition(bodyRB.position + forward * speed * Time.deltaTime);
                    }
                    if (Input.GetKey(KeyCode.DownArrow))
                    {
                        bodyRB.MovePosition(bodyRB.position - forward * speed * Time.deltaTime);
                    }
                    if (Input.GetKey(KeyCode.LeftArrow))
                    {
                        Quaternion rot = Quaternion.Euler(0f, -rotationSpeed * Time.deltaTime, 0f);
                        bodyRB.MoveRotation(bodyRB.rotation * rot);
                    }
                    if (Input.GetKey(KeyCode.RightArrow))
                    {
                        Quaternion rot = Quaternion.Euler(0f, rotationSpeed * Time.deltaTime, 0f);
                        bodyRB.MoveRotation(bodyRB.rotation * rot);
                    }
                }
                else
                {
                    bodyRB.AddForce(Vector3.zero);
                }
            }
            else
            {
                hasOriginalValues = false;
            }
        }
    }
}
