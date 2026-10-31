using UnityEngine;

namespace Temnorid.Village
{
	/// <summary>Turns the camera-facing text of a label towards the camera.</summary>
	public sealed class VillageLabel : MonoBehaviour
	{
		void LateUpdate()
		{
			var camera = Camera.main;
			if (camera == null) return;
			var away = transform.position - camera.transform.position;
			if (away.sqrMagnitude > 0.0001f) transform.rotation = Quaternion.LookRotation(away, Vector3.up);
		}
	}
}
