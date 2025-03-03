using System;
using System.Reflection;
using System.Text.RegularExpressions;
using MelonLoader;
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using SleddersTeleporterNs;

namespace SledTunerProject
{
    /// <summary>
    /// Handles teleportation functionality within the SledTuner mod, based on the original SleddersTeleporter
    /// </summary>
    public class TeleportManager
    {
        // Status properties
        public bool IsInitialized { get; private set; } = false;
        
        // Game object references - direct references to teleport controllers
        private GameObject _player = null;
        private TeleportMapViewController _teleportMapViewController = null;
        private TeleportMapController _teleportMapController = null;
        private ChatController2 _chatController = null;
        
        // Chat state
        private bool _chatOpen = false;
        
        // Teleport data
        private List<Vector3> _positionHistory = new List<Vector3>();
        private const int MAX_HISTORY = 10;
        
        // Default keys (can be customized later)
        private KeyCode _teleportKey = KeyCode.T;
        private KeyCode _savePositionKey = KeyCode.P;
        private KeyCode _teleportBackKey = KeyCode.Backspace;
        
        /// <summary>
        /// Initializes the teleport manager by finding required game objects
        /// </summary>
        public bool Initialize()
        {
            try
            {
                MelonLogger.Msg("[TeleportManager] Initializing teleport manager...");
                
                // Find controllers first - using FindAnyObjectByType like in original SleddersTeleporter
                FindMapControllers();
                FindChatController();
                FindPlayer();
                
                // Consider initialized if we have found the player
                IsInitialized = _player != null;
                
                if (IsInitialized)
                {
                    MelonLogger.Msg($"[TeleportManager] Teleport manager initialized successfully");
                    return true;
                }
                else
                {
                    MelonLogger.Error("[TeleportManager] Failed to initialize teleport manager - player not found");
                    return false;
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[TeleportManager] Error initializing teleport manager: {ex.Message}");
                IsInitialized = false;
                return false;
            }
        }
        
        /// <summary>
        /// Finds the map controllers, using exact approach from SleddersTeleporter
        /// </summary>
        private void FindMapControllers()
        {
            try
            {
                // Check if MapController exists in scene - using FindAnyObjectByType
                MapController mapController = UnityEngine.Object.FindAnyObjectByType<MapController>();
                
                if (mapController != null)
                {
                    MelonLogger.Msg("[TeleportManager] Found MapController object");
                    
                    // Create teleport controllers directly (exact approach from SleddersTeleporter)
                    _teleportMapViewController = new TeleportMapViewController();
                    _teleportMapController = new TeleportMapController();
                    
                    MelonLogger.Msg("[TeleportManager] Created TeleportMapViewController and TeleportMapController");
                }
                else
                {
                    MelonLogger.Warning("[TeleportManager] MapController not found - teleport functionality may be limited");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[TeleportManager] Error finding map controllers: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Finds the chat controller, using exact approach from SleddersTeleporter
        /// </summary>
        private void FindChatController()
        {
            try
            {
                // Direct approach to find ChatController2 as in SleddersTeleporter
                _chatController = UnityEngine.Object.FindAnyObjectByType<ChatController2>();
                
                if (_chatController != null)
                {
                    MelonLogger.Msg("[TeleportManager] Got chat controller instance!");
                }
                else
                {
                    MelonLogger.Warning("[TeleportManager] ChatController2 not found");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[TeleportManager] Error finding chat controller: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Finds the player object, using exact approach from SleddersTeleporter
        /// </summary>
        private void FindPlayer()
        {
            try
            {
                // The original uses FindGameObjectWithTag
                _player = GameObject.FindGameObjectWithTag("Player");
                
                if (_player != null)
                {
                    // Check for SnowmobileController like in the original
                    SnowmobileController snowmobileController = _player.GetComponent<SnowmobileController>();
                    if (snowmobileController != null)
                    {
                        MelonLogger.Msg("[TeleportManager] Found player with SnowmobileController");
                    }
                    else
                    {
                        MelonLogger.Warning("[TeleportManager] Player found but no SnowmobileController component");
                    }
                }
                else
                {
                    MelonLogger.Error("[TeleportManager] Could not find player object");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[TeleportManager] Error finding player: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Updates the chat state
        /// </summary>
        public void UpdateChatState()
        {
            try
            {
                if (_chatController != null)
                {
                    // Use the same field name as in the original SleddersTeleporter
                    _chatOpen = _chatController.FFHDACKPPIN;
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[TeleportManager] Error updating chat state: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Checks if the chat is open
        /// </summary>
        public bool IsChatOpen()
        {
            UpdateChatState();
            return _chatOpen;
        }
        
        /// <summary>
        /// Resets the teleport manager
        /// </summary>
        public void Reset()
        {
            MelonLogger.Msg("[TeleportManager] Resetting teleport manager");
            _player = null;
            _teleportMapViewController = null;
            _teleportMapController = null;
            _chatController = null;
            _positionHistory.Clear();
            _chatOpen = false;
            IsInitialized = false;
        }
        
        /// <summary>
        /// Process input for teleporting, should be called from Update method
        /// </summary>
        public void ProcessInput()
        {
            // Don't process if not initialized or chat is open
            if (!IsInitialized || IsChatOpen()) return;
            
            // Handle teleport to map cursor (T key)
            if (Input.GetKeyDown(_teleportKey))
            {
                TeleportToMapCursor();
            }
            
            // Handle save position (P key)
            if (Input.GetKeyDown(_savePositionKey))
            {
                SaveCurrentPosition();
            }
            
            // Handle teleport back (Backspace key)
            if (Input.GetKeyDown(_teleportBackKey))
            {
                TeleportBack();
            }
        }
        
        /// <summary>
        /// Gets the current position as a formatted string
        /// </summary>
        public string GetCurrentPositionString()
        {
            if (_player != null)
            {
                Vector3 pos = _player.transform.position;
                return GetPositionString(pos);
            }
            return "Player not found";
        }
        
        /// <summary>
        /// Formats a position vector as a string
        /// </summary>
        private string GetPositionString(Vector3 position)
        {
            return $"X: {position.x:F1}, Y: {position.y:F1}, Z: {position.z:F1}";
        }
        
        /// <summary>
        /// Saves the current position for later teleporting back
        /// </summary>
        public void SaveCurrentPosition()
        {
            if (_player != null)
            {
                Vector3 pos = _player.transform.position;
                _positionHistory.Add(pos);
                
                // Keep only the last MAX_HISTORY positions
                if (_positionHistory.Count > MAX_HISTORY)
                {
                    _positionHistory.RemoveAt(0);
                }
                
                MelonLogger.Msg($"[TeleportManager] Saved position: {GetPositionString(pos)}");
            }
        }
        
        /// <summary>
        /// Teleports the player to a specific position
        /// </summary>
        public bool TeleportToPosition(float x, float y, float z)
        {
            if (!IsInitialized || _player == null)
            {
                MelonLogger.Error("[TeleportManager] Cannot teleport - not initialized or player not found");
                return false;
            }
            
            try
            {
                Vector3 targetPosition = new Vector3(x, y, z);
                MelonLogger.Msg($"[TeleportManager] Teleporting to position: {GetPositionString(targetPosition)}");
                
                // Save current position before teleporting
                Vector3 currentPos = _player.transform.position;
                _positionHistory.Add(currentPos);
                if (_positionHistory.Count > MAX_HISTORY)
                {
                    _positionHistory.RemoveAt(0);
                }
                
                // If we have the teleport map view controller, use its teleport method
                if (_teleportMapViewController != null)
                {
                    _teleportMapViewController.teleportPlayer(targetPosition, Quaternion.Euler(0f, 0f, 0f));
                    MelonLogger.Msg($"[TeleportManager] Teleported using TeleportMapViewController");
                    return true;
                }
                
                // Fallback to direct transform modification
                MelonLogger.Warning("[TeleportManager] Using fallback teleport method - TeleportMapViewController not available");
                _player.transform.position = targetPosition;
                return true;
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[TeleportManager] Error teleporting: {ex.Message}");
                return false;
            }
        }
        
        /// <summary>
        /// Teleports back to the previous position
        /// </summary>
        public bool TeleportBack()
        {
            if (!IsInitialized || _player == null)
            {
                MelonLogger.Error("[TeleportManager] Cannot teleport back - not initialized or player not found");
                return false;
            }
            
            if (_positionHistory.Count > 0)
            {
                Vector3 lastPos = _positionHistory[_positionHistory.Count - 1];
                _positionHistory.RemoveAt(_positionHistory.Count - 1);
                
                MelonLogger.Msg($"[TeleportManager] Teleporting back to: {GetPositionString(lastPos)}");
                return TeleportToPosition(lastPos.x, lastPos.y, lastPos.z);
            }
            
            MelonLogger.Warning("[TeleportManager] No previous positions to teleport back to");
            return false;
        }
        
        /// <summary>
        /// Teleports to the map cursor position, using exact approach from SleddersTeleporter
        /// </summary>
        public bool TeleportToMapCursor()
        {
            if (!IsInitialized || _player == null)
            {
                MelonLogger.Warning("[TeleportManager] Cannot teleport to map cursor - not initialized or player not found");
                return false;
            }
            
            try
            {
                // Get the current MapViewController instance - exactly like in SleddersTeleporter
                MapViewController mapViewController = UnityEngine.Object.FindAnyObjectByType<MapViewController>();
                
                if (mapViewController != null && _teleportMapController != null)
                {
                    MelonLogger.Msg("[TeleportManager] Got mapViewController instance");
                    
                    // Get cursor position - exactly as in SleddersTeleporter
                    FieldInfo cursorField = typeof(MapViewController).GetField("FHJHDIABGPF", 
                        BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.GetField);
                    
                    if (cursorField == null)
                    {
                        MelonLogger.Error("[TeleportManager] Could not find map cursor field");
                        return false;
                    }
                    
                    Vector2 cursorMapPosition = (Vector2)cursorField.GetValue(mapViewController);
                    MelonLogger.Msg($"[TeleportManager] Cursor map position: {cursorMapPosition}");
                    
                    // Convert to world position using our teleport map controller
                    Vector2 worldPos = _teleportMapController.mapToWorldPosition(cursorMapPosition);
                    MelonLogger.Msg($"[TeleportManager] World position: {worldPos}");
                    
                    // Teleport using teleport view controller
                    return TeleportToPosition(worldPos.x, 0f, worldPos.y);
                }
                else
                {
                    MelonLogger.Warning("[TeleportManager] Cannot teleport to map cursor - map controllers not found");
                    return false;
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[TeleportManager] Error teleporting to map cursor: {ex.Message}");
                return false;
            }
        }
        
        /// <summary>
        /// Teleports to a specific map position
        /// </summary>
        public bool TeleportToMapPosition(Vector2 mapPosition)
        {
            if (!IsInitialized || _player == null || _teleportMapController == null)
            {
                MelonLogger.Error("[TeleportManager] Cannot teleport to map position - required components not found");
                return false;
            }
            
            try
            {
                // Convert to world position using our teleport map controller
                Vector2 worldPos = _teleportMapController.mapToWorldPosition(mapPosition);
                MelonLogger.Msg($"[TeleportManager] Map position {mapPosition} → World position: {worldPos}");
                
                // Teleport to the world position
                return TeleportToPosition(worldPos.x, 0f, worldPos.y);
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[TeleportManager] Error teleporting to map position: {ex.Message}");
                return false;
            }
        }
        
        /// <summary>
        /// Sets the key used for teleporting to map cursor
        /// </summary>
        public void SetTeleportKey(KeyCode key)
        {
            _teleportKey = key;
            MelonLogger.Msg($"[TeleportManager] Teleport key set to: {key}");
        }
        
        /// <summary>
        /// Sets the key used for saving position
        /// </summary>
        public void SetSavePositionKey(KeyCode key)
        {
            _savePositionKey = key;
            MelonLogger.Msg($"[TeleportManager] Save position key set to: {key}");
        }
        
        /// <summary>
        /// Sets the key used for teleporting back
        /// </summary>
        public void SetTeleportBackKey(KeyCode key)
        {
            _teleportBackKey = key;
            MelonLogger.Msg($"[TeleportManager] Teleport back key set to: {key}");
        }
    }
} 