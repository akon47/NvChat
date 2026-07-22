using System;

namespace NvChat.Models
{
    /// <summary>
    /// build.nvidia.com 에서 제공하는 하나의 모델 정보.
    /// </summary>
    public class NvModel
    {
        #region Properties

        /// <summary>
        /// 모델 ID (예: "meta/llama-3.3-70b-instruct"). API 호출에 사용한다.
        /// </summary>
        public string Id { get; set; }

        /// <summary>
        /// 게시자 (ID 의 '/' 앞부분, 예: "meta").
        /// </summary>
        public string Publisher { get; set; }

        /// <summary>
        /// 모델 이름 (ID 의 '/' 뒷부분, 예: "llama-3.3-70b-instruct").
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// UI 표시용 이름.
        /// </summary>
        public string DisplayName { get; set; }

        #endregion


        #region Helpers

        public static NvModel FromId(string id)
        {
            id = (id ?? string.Empty).Trim();

            var slash = id.IndexOf('/');
            string publisher;
            string name;

            if (slash > 0)
            {
                publisher = id.Substring(0, slash);
                name = id.Substring(slash + 1);
            }
            else
            {
                publisher = string.Empty;
                name = id;
            }

            return new NvModel
            {
                Id = id,
                Publisher = publisher,
                Name = name,
                DisplayName = string.IsNullOrEmpty(publisher) ? name : $"{name}  ·  {publisher}"
            };
        }

        public override bool Equals(object obj)
        {
            return obj is NvModel other && string.Equals(other.Id, Id, StringComparison.OrdinalIgnoreCase);
        }

        public override int GetHashCode()
        {
            return Id == null ? 0 : StringComparer.OrdinalIgnoreCase.GetHashCode(Id);
        }

        public override string ToString()
        {
            return DisplayName ?? Id;
        }

        #endregion
    }
}
