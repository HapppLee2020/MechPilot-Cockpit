using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using EPDM.Interop.epdm;

namespace SwAgentAddin
{
    /// <summary>
    /// 本地 PDM COM：读取工作流状态（与 SW CustomPropertyManager 同进程、不走 MCP）。
    /// </summary>
    public class PdmComHelper
    {
        private readonly string _vaultName;
        private IEdmVault5 _vault;
        private string _loggedVaultName;
        private readonly Dictionary<string, bool> _vaultMembershipCache =
            new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        public PdmComHelper(string vaultName = null)
        {
            _vaultName = string.IsNullOrWhiteSpace(vaultName) ? "XtalPi-PDM" : vaultName;
        }

        /// <summary>
        /// 文件是否已在 PDM Vault 中管理（本地路径可解析到 vault 文件）。
        /// </summary>
        public bool IsFileInVault(string localFilePath)
        {
            if (string.IsNullOrWhiteSpace(localFilePath) || localFilePath == "不可用")
                return false;

            bool cached;
            if (_vaultMembershipCache.TryGetValue(localFilePath, out cached))
                return cached;

            IEdmFile5 file = null;
            try
            {
                file = TryResolveFile(localFilePath);
                bool inVault = file != null;
                _vaultMembershipCache[localFilePath] = inVault;
                return inVault;
            }
            finally
            {
                ReleaseCom(file);
            }
        }

        /// <summary>
        /// 读取 PDM 工作流状态名，如「机械图纸拟定中」「图纸生效」。
        /// </summary>
        public string GetWorkflowStateName(string localFilePath)
        {
            if (string.IsNullOrWhiteSpace(localFilePath) || localFilePath == "不可用")
                return "";

            IEdmFile5 file = null;
            try
            {
                file = TryResolveFile(localFilePath);
                if (file == null)
                {
                    SwAgentAddin.WriteTrace("[PdmComHelper] file not in vault: " + localFilePath);
                    return "";
                }

                IEdmState5 state = file.CurrentState;
                string name = state?.Name ?? "";
                SwAgentAddin.WriteTrace(string.Format("[PdmComHelper] {0} -> {1}", Path.GetFileName(localFilePath), name));
                return name;
            }
            catch (Exception ex)
            {
                SwAgentAddin.WriteTrace("[PdmComHelper] GetWorkflowStateName failed: " + ex.Message);
                return "";
            }
            finally
            {
                ReleaseCom(file);
            }
        }

        private IEdmFile5 TryResolveFile(string localFilePath)
        {
            IEdmFile5 file = null;
            IEdmFolder5 folder = null;
            try
            {
                IEdmVault5 vault = EnsureVault();
                file = vault.GetFileFromPath(localFilePath, out folder);
                if (file == null)
                    file = SearchFileByLocalPath(vault, localFilePath);

                if (file != null)
                    _vaultMembershipCache[localFilePath] = true;
                else
                    _vaultMembershipCache[localFilePath] = false;

                if (file != null)
                {
                    // 调用方负责 ReleaseCom(file)
                    var resolved = file;
                    file = null;
                    return resolved;
                }

                return null;
            }
            catch
            {
                _vaultMembershipCache[localFilePath] = false;
                return null;
            }
            finally
            {
                ReleaseCom(folder);
                ReleaseCom(file);
            }
        }

        private IEdmVault5 EnsureVault()
        {
            if (_vault != null && string.Equals(_loggedVaultName, _vaultName, StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    if (_vault.IsLoggedIn) return _vault;
                }
                catch { ReleaseVault(); }
            }

            ReleaseVault();
            Type vaultType = Type.GetTypeFromProgID("ConisioLib.EdmVault");
            if (vaultType == null)
                throw new InvalidOperationException("未找到 PDM COM（ConisioLib.EdmVault），请确认已安装 PDM 客户端。");

            dynamic dynVault = Activator.CreateInstance(vaultType);
            try
            {
                dynVault.LoginAuto(_vaultName, 0);
            }
            catch (COMException loginEx)
            {
                throw new InvalidOperationException(
                    "PDM Vault '" + _vaultName + "' 自动登录失败: " + loginEx.Message, loginEx);
            }

            _vault = (IEdmVault5)dynVault;
            _loggedVaultName = _vaultName;
            return _vault;
        }

        private static IEdmFile5 SearchFileByLocalPath(IEdmVault5 vault, string localFilePath)
        {
            string fileName = Path.GetFileName(localFilePath);
            if (string.IsNullOrEmpty(fileName)) return null;

            IEdmSearch5 search = null;
            IEdmFile5 matched = null;
            try
            {
                search = vault.CreateSearch();
                search.FileName = fileName;
                search.FindFiles = true;
                search.FindFolders = false;

                IEdmSearchResult5 hit = search.GetFirstResult();
                while (hit != null && matched == null)
                {
                    IEdmFile5 candidate = null;
                    IEdmFolder5 folder = null;
                    try
                    {
                        string resultPath = hit.Path ?? "";
                        if (!string.IsNullOrEmpty(resultPath))
                        {
                            candidate = vault.GetFileFromPath(resultPath, out folder);
                            if (candidate != null)
                            {
                                int folderId = folder?.ID ?? 0;
                                if (folderId <= 0)
                                {
                                    try
                                    {
                                        IEdmPos5 fpos = candidate.GetFirstFolderPosition();
                                        if (fpos != null && !fpos.IsNull)
                                            folderId = candidate.GetNextFolder(fpos).ID;
                                    }
                                    catch { }
                                }

                                string localPath = "";
                                try { localPath = candidate.GetLocalPath(folderId) ?? ""; } catch { }

                                if (PathsEqual(localPath, localFilePath))
                                {
                                    matched = candidate;
                                    candidate = null;
                                }
                            }
                        }
                    }
                    finally
                    {
                        ReleaseCom(folder);
                        ReleaseCom(candidate);
                        ReleaseCom(hit);
                    }

                    if (matched == null)
                        hit = search.GetNextResult();
                }
            }
            finally
            {
                ReleaseCom(search);
            }

            return matched;
        }

        private static bool PathsEqual(string left, string right)
        {
            if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right)) return false;
            try
            {
                return string.Equals(
                    Path.GetFullPath(left.Trim()),
                    Path.GetFullPath(right.Trim()),
                    StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);
            }
        }

        private void ReleaseVault()
        {
            if (_vault == null) return;
            try { ((dynamic)_vault).Logout(); } catch { }
            ReleaseCom(_vault);
            _vault = null;
            _loggedVaultName = null;
        }

        private static void ReleaseCom(object comObj)
        {
            if (comObj == null) return;
            try
            {
                if (Marshal.IsComObject(comObj))
                    Marshal.ReleaseComObject(comObj);
            }
            catch { }
        }
    }
}
