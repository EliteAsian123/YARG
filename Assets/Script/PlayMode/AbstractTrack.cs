using TMPro;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using YARG.Data;
using YARG.UI;

namespace YARG.PlayMode {
	public abstract class AbstractTrack : MonoBehaviour {
		protected const float TRACK_SPAWN_OFFSET = 3f;

		public delegate void StarpowerMissAction();
		public event StarpowerMissAction StarpowerMissEvent;

		public PlayerManager.Player player;
		public float RelativeTime => Play.Instance.SongTime + ((TRACK_SPAWN_OFFSET + 1.75f) / player.trackSpeed);

		[SerializeField]
		protected Camera trackCamera;

		[Space]
		[SerializeField]
		protected MeshRenderer trackRenderer;
		[SerializeField]
		protected Transform hitWindow;

		[Space]
		[SerializeField]
		protected TextMeshPro comboText;
		[SerializeField]
		protected MeshRenderer comboMeterRenderer;
		[SerializeField]
		protected MeshRenderer starpowerBarTop;

		public EventInfo StarpowerSection {
			get;
			protected set;
		} = null;

		protected float starpowerCharge;
		protected bool starpowerActive;

		private int _combo = 0;
		protected int Combo {
			get => _combo;
			set {
				_combo = value;

				// End starpower if combo ends
				if (StarpowerSection?.time <= Play.Instance.SongTime && value == 0) {
					StarpowerSection = null;
					StarpowerMissEvent?.Invoke();
				}
			}
		}

		protected int MaxMultiplier => (player.chosenInstrument == "bass" ? 6 : 4) * (starpowerActive ? 2 : 1);
		protected int Multiplier => Mathf.Min((Combo / 10 + 1) * (starpowerActive ? 2 : 1), MaxMultiplier);

		private bool _stopAudio = false;
		protected bool StopAudio {
			set {
				if (value == _stopAudio) {
					return;
				}

				_stopAudio = value;

				if (!value) {
					Play.Instance.RaiseAudio(player.chosenInstrument);
				} else {
					Play.Instance.LowerAudio(player.chosenInstrument);
				}
			}
		}

		protected bool Beat {
			get;
			private set;
		}

		private void Awake() {
			// Set up render texture
			var descriptor = new RenderTextureDescriptor(
				Screen.width, Screen.height,
				RenderTextureFormat.DefaultHDR
			);
			descriptor.mipCount = 0;
			var renderTexture = new RenderTexture(descriptor);
			trackCamera.targetTexture = renderTexture;

			// Set up camera
			var info = trackCamera.GetComponent<UniversalAdditionalCameraData>();
			if (GameManager.Instance.LowQualityMode) {
				info.antialiasing = AntialiasingMode.None;
			} else {
				info.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
				info.antialiasingQuality = AntialiasingQuality.Low;
			}
		}

		private void Start() {
			player.track = this;

			player.inputStrategy.StarpowerEvent += StarpowerAction;
			Play.Instance.BeatEvent += BeatAction;

			GameUI.Instance.AddTrackImage(trackCamera.targetTexture);

			// Adjust hit window
			var scale = hitWindow.localScale;
			hitWindow.localScale = new(scale.x, Play.HIT_MARGIN * player.trackSpeed * 2f, scale.z);

			StartTrack();
		}

		protected abstract void StartTrack();

		protected virtual void OnDestroy() {
			// Release render texture
			trackCamera.targetTexture.Release();

			player.inputStrategy.StarpowerEvent -= StarpowerAction;
			Play.Instance.BeatEvent -= BeatAction;
		}

		private void Update() {
			UpdateMaterial();
			UpdateTrack();

			Beat = false;
		}

		protected abstract void UpdateTrack();

		private void UpdateMaterial() {
			// Update track UV
			var trackMaterial = trackRenderer.material;
			var oldOffset = trackMaterial.GetVector("TexOffset");
			float movement = Time.deltaTime * player.trackSpeed / 4f;
			trackMaterial.SetVector("TexOffset", new(oldOffset.x, oldOffset.y - movement));

			// Update track groove
			float currentGroove = trackMaterial.GetFloat("GrooveState");
			if (Multiplier >= MaxMultiplier) {
				trackMaterial.SetFloat("GrooveState", Mathf.Lerp(currentGroove, 1f, Time.deltaTime * 5f));
			} else {
				trackMaterial.SetFloat("GrooveState", Mathf.Lerp(currentGroove, 0f, Time.deltaTime * 3f));
			}

			// Update track starpower
			float currentStarpower = trackMaterial.GetFloat("StarpowerState");
			if (starpowerActive) {
				trackMaterial.SetFloat("StarpowerState", Mathf.Lerp(currentStarpower, 1f, Time.deltaTime * 2f));
			} else {
				trackMaterial.SetFloat("StarpowerState", Mathf.Lerp(currentStarpower, 0f, Time.deltaTime * 4f));
			}

			// Update starpower bar
			var starpowerMat = starpowerBarTop.material;
			starpowerMat.SetFloat("Fill", starpowerCharge);
			if (Beat) {
				float pulseAmount = 0f;
				if (starpowerActive) {
					pulseAmount = 0.25f;
				} else if (!starpowerActive && starpowerCharge >= 0.5f) {
					pulseAmount = 1f;
				}

				starpowerMat.SetFloat("Pulse", pulseAmount);
			} else {
				float currentPulse = starpowerMat.GetFloat("Pulse");
				starpowerMat.SetFloat("Pulse", Mathf.Lerp(currentPulse, 0f, Time.deltaTime * 16f));
			}
		}

		private void BeatAction() {
			Beat = true;
		}

		private void StarpowerAction() {
			if (!starpowerActive && starpowerCharge >= 0.5f) {
				starpowerActive = true;
			}
		}

		protected float CalcLagCompensation(float currentTime, float noteTime) {
			return (currentTime - noteTime) * player.trackSpeed;
		}
	}
}