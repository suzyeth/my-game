using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FartSymphony.Core
{
    /// <summary>
    /// Captures player key-presses and converts them into typed events carrying
    /// precise audio-clock timestamps.
    ///
    /// State machine:
    ///   Active     — normal play; Release and Pause events fire.
    ///   Paused     — Release events suppressed; Pause event still fires.
    ///   Disabled   — all events suppressed.
    ///   Calibrating — same as Active; used by AudioCalibration screen.
    ///
    /// ADR-0002: publishes C# events. Downstream systems subscribe in OnEnable,
    /// unsubscribe in OnDisable.
    /// </summary>
    public sealed class InputSystem : MonoBehaviour
    {
        // ── Inspector knobs ───────────────────────────────────────────────────
        [Header("Latency Compensation")]
        [Tooltip("Subtracted from the raw dspTime stamp before broadcasting. " +
                 "Positive = player perceives input as late; negative = early. " +
                 "Set by AudioCalibration in Vertical Slice. MVP default: 0.")]
        [SerializeField] [Range(-100f, 100f)]
        private float _inputLatencyOffsetMs = 0f;

        // ── Events (ADR-0002: C# events, not UnityEvent) ──────────────────────
        /// <summary>Fired when the release key is pressed while Active or Calibrating.</summary>
        public event Action<InputEvent> OnReleasePressed;

        /// <summary>Fired when the pause key is pressed while not Disabled.</summary>
        public event Action OnPausePressed;

        // ── State ─────────────────────────────────────────────────────────────
        public enum State { Active, Paused, Disabled, Calibrating }

        private State _state = State.Disabled;
        public  State CurrentState => _state;

        public float InputLatencyOffsetMs => _inputLatencyOffsetMs;

        // ── Input actions (created in code; no InputActionAsset required) ─────
        private InputAction _releaseAction;
        private InputAction _pauseAction;

        // ── Unity lifecycle ───────────────────────────────────────────────────

        private void Awake()
        {
            _releaseAction = new InputAction("Release", InputActionType.Button,
                binding: "<Keyboard>/space");
            _pauseAction   = new InputAction("Pause", InputActionType.Button,
                binding: "<Keyboard>/escape");
        }

        private void OnEnable()
        {
            _releaseAction.performed += HandleReleasePerformed;
            _pauseAction.performed   += HandlePausePerformed;
            _releaseAction.Enable();
            _pauseAction.Enable();
        }

        private void OnDisable()
        {
            _releaseAction.performed -= HandleReleasePerformed;
            _pauseAction.performed   -= HandlePausePerformed;
            _releaseAction.Disable();
            _pauseAction.Disable();
        }

        private void OnDestroy()
        {
            _releaseAction?.Dispose();
            _pauseAction?.Dispose();
        }

        // ── Public API ────────────────────────────────────────────────────────

        public void SetActive()      => _state = State.Active;
        public void SetPaused()      => _state = State.Paused;
        public void SetDisabled()    => _state = State.Disabled;
        public void SetCalibrating() => _state = State.Calibrating;

        /// <summary>
        /// Updates the latency offset at runtime (called by AudioCalibration in VS).
        /// </summary>
        public void SetLatencyOffset(float offsetMs) => _inputLatencyOffsetMs = offsetMs;

        // ── Input callbacks ───────────────────────────────────────────────────

        private void HandleReleasePerformed(InputAction.CallbackContext ctx)
        {
            if (_state != State.Active && _state != State.Calibrating) return;

            // Capture dspTime immediately — this is the authoritative audio clock.
            // AudioSettings.dspTime is in seconds; convert to ms.
            double rawDspMs      = AudioSettings.dspTime * 1000.0;
            double adjustedMs    = rawDspMs - _inputLatencyOffsetMs;

            var inputEvent = new InputEvent(adjustedMs, rawDspMs);
            OnReleasePressed?.Invoke(inputEvent);

            Debug.Log($"[InputSystem] Release: raw={rawDspMs:F2}ms  adj={adjustedMs:F2}ms  " +
                      $"offset={_inputLatencyOffsetMs:F1}ms");
        }

        private void HandlePausePerformed(InputAction.CallbackContext ctx)
        {
            if (_state == State.Disabled) return;
            OnPausePressed?.Invoke();
            Debug.Log("[InputSystem] Pause pressed");
        }
    }
}
