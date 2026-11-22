using System.Collections.Generic;
using KLTN.Audio;
using KLTN.Game.Domain;
using KLTN.Game.Networking;
using UnityEngine;

namespace KLTN.Game.Presentation
{
    public sealed partial class MatchDebugControls : MonoBehaviour
    {
        #region Constants

        // Tên ô theo góc nhìn người chơi: ô 0 nằm bên trái.
        private static readonly string[] BoardSlotNames = { "trái", "giữa", "phải" };

        #endregion

        #region Rejection Feedback

        /// <summary>
        /// Plays the rejection sound for a refused player action and, when a message is
        /// given, shows it in the local status banner.
        /// </summary>
        /// <remarks>
        /// An empty message keeps the banner unchanged, so silent rejections only
        /// produce the sound.
        /// </remarks>
        private void RejectLocalAction(string message = null)
        {
            PlayActionRejectedSfx();

            if (!string.IsNullOrWhiteSpace(message))
            {
                SetLocalStatus(message);
            }
        }

        /// <summary>
        /// Plays the rejection clip on the SFX channel, reading the volume at play
        /// time so settings changes apply to the next rejection.
        /// </summary>
        private void PlayActionRejectedSfx()
        {
            float volume = AudioVolumeSettings.SfxVolume;

            if (actionRejectedSfx == null || volume <= 0f)
            {
                return;
            }

            if (actionRejectedSource == null)
            {
                // Nguồn riêng để không chiếm AudioSource của component khác.
                actionRejectedSource = gameObject.AddComponent<AudioSource>();
                actionRejectedSource.playOnAwake = false;
                actionRejectedSource.loop = false;
                actionRejectedSource.spatialBlend = 0f;
            }

            actionRejectedSource.PlayOneShot(actionRejectedSfx, volume);
        }

        #endregion

        #region Status Messages

        private string BuildSummonUnavailableStatus(MatchSnapshotDto snapshot)
        {
            // Lệnh đang gửi: chỉ phát âm báo, không hiện chữ.
            if (snapshot == null || submitting)
            {
                return string.Empty;
            }

            return snapshot.viewerCanDeclareBlock
                ? "Không thể thực hiện triệu hồi khi đối thủ đang tấn công."
                : "Chưa đến lượt bạn.";
        }

        /// <summary>
        /// Lists the board slots that currently hold an opponent attacker, using
        /// left/middle/right names joined with "hoặc".
        /// </summary>
        /// <returns>An empty string when no slot can be defended.</returns>
        private static string BuildDefendableSlotsStatus(MatchSnapshotDto snapshot)
        {
            var names = new List<string>(MatchState.BoardSlotCount);

            for (int slot = 0; slot < MatchState.BoardSlotCount; slot++)
            {
                if (
                    slot < BoardSlotNames.Length
                    && HasOpponentAttackerAtSlot(snapshot, slot)
                )
                {
                    names.Add(BoardSlotNames[slot]);
                }
            }

            if (names.Count == 0)
            {
                return string.Empty;
            }

            string last = names[names.Count - 1];
            string slots = last;

            if (names.Count > 1)
            {
                string leading = string.Join(", ", names.GetRange(0, names.Count - 1));
                slots = $"{leading} hoặc {last}";
            }

            return $"Chỉ có thể phòng thủ tại ô {slots}.";
        }

        #endregion
    }
}
