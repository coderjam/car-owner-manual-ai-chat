<template>
  <div class="admin-shell">
    <aside class="admin-sidebar">
      <div class="sidebar-brand">
        <div class="brand-mark compact">
          <ShieldCheck :size="20" />
        </div>
        <div>
          <strong>内容中心</strong>
          <span>MANUAL OPS</span>
        </div>
      </div>

      <nav class="admin-nav-list" aria-label="后台导航">
        <button class="admin-nav active" type="button" aria-label="手册管理">
          <FileStack :size="17" />
          <span>手册管理</span>
        </button>
        <button
          class="admin-nav"
          type="button"
          aria-label="打开用户端"
          @click="$emit('goUser')"
        >
          <MessageSquare :size="17" />
          <span>用户端</span>
        </button>
      </nav>

      <button
        class="sidebar-footer-link"
        type="button"
        aria-label="退出登录"
        @click="$emit('logout')"
      >
        <LogOut :size="16" />
        <span>退出登录</span>
      </button>
    </aside>

    <main class="admin-main">
      <header class="admin-header">
        <div>
          <span class="header-kicker">知识库维护</span>
          <h1>用户手册管理</h1>
          <p>管理车型手册、整页图片和知识片段</p>
        </div>
        <el-tooltip content="刷新数据" placement="bottom">
          <el-button
            class="header-icon-button"
            circle
            :loading="refreshing"
            aria-label="刷新数据"
            @click="refreshAll()"
          >
            <RefreshCw v-if="!refreshing" :size="17" />
          </el-button>
        </el-tooltip>
      </header>

      <div class="admin-content">
        <section class="admin-metrics" aria-label="知识库概况">
          <div class="metric-item">
            <span>手册总数</span>
            <strong>{{ manuals.length }}</strong>
            <small>份 PDF</small>
          </div>
          <div class="metric-item">
            <span>可用手册</span>
            <strong>{{ completedManuals }}</strong>
            <small>解析完成</small>
          </div>
          <div class="metric-item">
            <span>手册页数</span>
            <strong>{{ totalPages }}</strong>
            <small>页原文</small>
          </div>
          <div class="metric-item accent">
            <span>知识片段</span>
            <strong>{{ totalChunks }}</strong>
            <small>可检索</small>
          </div>
        </section>

        <section class="admin-panel upload-panel">
          <div class="panel-intro">
            <span class="section-index">01 / 导入</span>
            <h2>添加用户手册</h2>
            <p>选择对应车型后上传 PDF，系统会生成文本、知识片段和整页图片。</p>
          </div>

          <form class="upload-form" @submit.prevent="submitManual">
            <label class="form-field">
              <span>对应车型</span>
              <el-select v-model="selectedVehicleId" placeholder="选择车型">
                <el-option
                  v-for="vehicle in vehicles"
                  :key="vehicle.id"
                  :label="formatVehicle(vehicle)"
                  :value="vehicle.id"
                />
              </el-select>
            </label>

            <label class="form-field">
              <span>资料来源 <small>选填</small></span>
              <el-input v-model="sourceUrl" placeholder="厂商手册下载地址" />
            </label>

            <div class="form-field file-field">
              <span>PDF 文件</span>
              <input
                ref="fileInputRef"
                class="file-input-hidden"
                type="file"
                accept="application/pdf"
                @change="handleFileChange"
              />
              <button
                class="file-dropzone"
                :class="{ dragging: isDragging, selected: uploadFile }"
                type="button"
                @click="chooseFile"
                @dragenter.prevent="isDragging = true"
                @dragover.prevent="isDragging = true"
                @dragleave.prevent="isDragging = false"
                @drop.prevent="handleFileDrop"
              >
                <span class="file-icon">
                  <FileCheck2 v-if="uploadFile" :size="22" />
                  <UploadCloud v-else :size="22" />
                </span>
                <span class="file-copy">
                  <strong>{{ uploadFile?.name ?? '选择 PDF 文件' }}</strong>
                  <small>
                    {{ uploadFile ? formatFileSize(uploadFile.size) : '单个文件不超过 200 MB' }}
                  </small>
                </span>
                <span class="file-action">{{ uploadFile ? '更换' : '浏览' }}</span>
              </button>
            </div>

            <div class="upload-submit">
              <el-button
                native-type="submit"
                :disabled="!selectedVehicleId || !uploadFile"
                :loading="uploading"
              >
                <Sparkles :size="16" />
                上传并生成知识库
              </el-button>
            </div>
          </form>
        </section>

        <section class="admin-panel library-panel">
          <div class="library-heading">
            <div>
              <span class="section-index">02 / 资料库</span>
              <h2>已导入手册</h2>
            </div>
            <span v-if="hasActiveJobs" class="library-live">
              <LoaderCircle :size="13" /> 正在更新解析进度
            </span>
            <span v-else class="library-count">{{ manuals.length }} 份文档</span>
          </div>

          <el-table
            :data="manuals"
            class="manual-table"
            row-key="id"
            empty-text="还没有导入用户手册"
          >
            <el-table-column label="手册" min-width="300">
              <template #default="{ row }">
                <div class="manual-name-cell">
                  <span class="document-icon"><FileText :size="18" /></span>
                  <div>
                    <strong>{{ row.fileName }}</strong>
                    <span>{{ manualVehicleLabel(row.vehicleId) }}</span>
                  </div>
                </div>
              </template>
            </el-table-column>
            <el-table-column label="状态" width="130">
              <template #default="{ row }">
                <span class="status-badge" :class="row.status">
                  <i />
                  {{ statusText(row.status) }}
                </span>
              </template>
            </el-table-column>
            <el-table-column label="处理结果" min-width="230">
              <template #default="{ row }">
                <div class="processing-result">
                  <span><strong>{{ row.totalPages }}</strong> 页</span>
                  <span><strong>{{ row.generatedPageImages }}</strong> 页图</span>
                  <span><strong>{{ row.knowledgeChunks }}</strong> 片段</span>
                </div>
              </template>
            </el-table-column>
            <el-table-column label="导入时间" width="150">
              <template #default="{ row }">
                <time class="manual-date">{{ formatDate(row.createTime) }}</time>
              </template>
            </el-table-column>
            <el-table-column label="" width="112" align="right">
              <template #default="{ row }">
                <div class="row-actions">
                  <el-tooltip content="重新解析" placement="top">
                    <el-button
                      circle
                      aria-label="重新解析"
                      :disabled="isManualActionDisabled(row)"
                      :loading="isManualBusy(row.id)"
                      @click="reprocessManual(row.id)"
                    >
                      <RotateCw v-if="!isManualBusy(row.id)" :size="15" />
                    </el-button>
                  </el-tooltip>
                  <el-tooltip content="删除手册" placement="top">
                    <el-button
                      circle
                      type="danger"
                      plain
                      aria-label="删除手册"
                      :disabled="isManualActionDisabled(row)"
                      @click="deleteManual(row)"
                    >
                      <Trash2 :size="15" />
                    </el-button>
                  </el-tooltip>
                </div>
              </template>
            </el-table-column>
          </el-table>

          <div class="manual-mobile-list">
            <article v-for="manual in manuals" :key="manual.id" class="manual-mobile-item">
              <div class="mobile-manual-heading">
                <span class="document-icon"><FileText :size="18" /></span>
                <div>
                  <strong>{{ manual.fileName }}</strong>
                  <span>{{ manualVehicleLabel(manual.vehicleId) }}</span>
                </div>
              </div>
              <div class="mobile-manual-status">
                <span class="status-badge" :class="manual.status">
                  <i />
                  {{ statusText(manual.status) }}
                </span>
                <time>{{ formatDate(manual.createTime) }}</time>
              </div>
              <div class="processing-result">
                <span><strong>{{ manual.totalPages }}</strong> 页</span>
                <span><strong>{{ manual.generatedPageImages }}</strong> 页图</span>
                <span><strong>{{ manual.knowledgeChunks }}</strong> 片段</span>
              </div>
              <div class="mobile-manual-actions">
                <el-button
                  :disabled="isManualActionDisabled(manual)"
                  :loading="isManualBusy(manual.id)"
                  @click="reprocessManual(manual.id)"
                >
                  <RotateCw v-if="!isManualBusy(manual.id)" :size="15" />
                  重新解析
                </el-button>
                <el-button
                  type="danger"
                  plain
                  :disabled="isManualActionDisabled(manual)"
                  @click="deleteManual(manual)"
                >
                  <Trash2 :size="15" />
                  删除
                </el-button>
              </div>
            </article>
            <div v-if="manuals.length === 0" class="manual-empty">
              <FilePlus2 :size="24" />
              <span>还没有导入用户手册</span>
            </div>
          </div>
        </section>
      </div>
    </main>
  </div>
</template>

<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref } from 'vue';
import { ElMessage, ElMessageBox } from 'element-plus';
import {
  FileCheck2,
  FilePlus2,
  FileStack,
  FileText,
  LogOut,
  LoaderCircle,
  MessageSquare,
  RefreshCw,
  RotateCw,
  ShieldCheck,
  Sparkles,
  Trash2,
  UploadCloud
} from 'lucide-vue-next';
import {
  deleteAdminManual,
  getAdminManuals,
  getVehicles,
  reprocessAdminManual,
  uploadManual
} from '../api';
import type { Manual, Vehicle } from '../types';

const props = defineProps<{
  adminToken: string;
}>();

const emit = defineEmits<{
  logout: [];
  goUser: [];
}>();

const vehicles = ref<Vehicle[]>([]);
const manuals = ref<Manual[]>([]);
const selectedVehicleId = ref<number>();
const sourceUrl = ref('');
const uploadFile = ref<File | null>(null);
const fileInputRef = ref<HTMLInputElement | null>(null);
const uploading = ref(false);
const refreshing = ref(false);
const isDragging = ref(false);
const busyManualIds = ref(new Set<number>());
let statusRefreshTimer: number | undefined;

const completedManuals = computed(() => {
  return manuals.value.filter((manual) => manual.status === 'completed').length;
});

const totalPages = computed(() => {
  return manuals.value.reduce((total, manual) => total + manual.totalPages, 0);
});

const totalChunks = computed(() => {
  return manuals.value.reduce((total, manual) => total + manual.knowledgeChunks, 0);
});

const hasActiveJobs = computed(() => {
  return manuals.value.some((manual) => ['uploaded', 'processing'].includes(manual.status));
});

onMounted(async () => {
  await refreshAll();
});

onBeforeUnmount(() => {
  if (statusRefreshTimer !== undefined) {
    window.clearTimeout(statusRefreshTimer);
  }
});

function formatVehicle(vehicle: Vehicle): string {
  return `${vehicle.year} ${vehicle.brand}${vehicle.model} ${vehicle.engine} ${vehicle.configuration}`;
}

function manualVehicleLabel(vehicleId: number): string {
  const vehicle = vehicles.value.find((item) => item.id === vehicleId);
  return vehicle ? formatVehicle(vehicle) : `车型 ID ${vehicleId}`;
}

function formatDate(value: string): string {
  return new Date(value).toLocaleDateString('zh-CN', {
    year: 'numeric',
    month: '2-digit',
    day: '2-digit'
  });
}

function formatFileSize(bytes: number): string {
  if (bytes < 1024 * 1024) {
    return `${Math.max(1, Math.round(bytes / 1024))} KB`;
  }

  return `${(bytes / 1024 / 1024).toFixed(1)} MB`;
}

async function refreshAll(showLoading = true) {
  if (showLoading) {
    refreshing.value = true;
  }

  try {
    vehicles.value = await getVehicles();
    selectedVehicleId.value = selectedVehicleId.value ?? vehicles.value[0]?.id;
    manuals.value = await getAdminManuals(props.adminToken);
  } catch (error) {
    if (isUnauthorizedError(error)) {
      ElMessage.error('后台登录已失效');
      emit('logout');
      return;
    }

    if (showLoading) {
      ElMessage.error('后台数据加载失败');
    }
  } finally {
    if (showLoading) {
      refreshing.value = false;
    }
    scheduleStatusRefresh();
  }
}

function scheduleStatusRefresh() {
  if (statusRefreshTimer !== undefined) {
    window.clearTimeout(statusRefreshTimer);
    statusRefreshTimer = undefined;
  }

  if (!hasActiveJobs.value) {
    return;
  }

  statusRefreshTimer = window.setTimeout(() => {
    void refreshAll(false);
  }, 3000);
}

function chooseFile() {
  fileInputRef.value?.click();
}

function handleFileChange(event: Event) {
  const input = event.target as HTMLInputElement;
  setUploadFile(input.files?.[0] ?? null);
}

function handleFileDrop(event: DragEvent) {
  isDragging.value = false;
  setUploadFile(event.dataTransfer?.files?.[0] ?? null);
}

function setUploadFile(file: File | null) {
  if (file && !file.name.toLowerCase().endsWith('.pdf')) {
    uploadFile.value = null;
    if (fileInputRef.value) {
      fileInputRef.value.value = '';
    }
    ElMessage.warning('只支持上传 PDF 用户手册');
    return;
  }

  if (file && file.size > 200 * 1024 * 1024) {
    uploadFile.value = null;
    if (fileInputRef.value) {
      fileInputRef.value.value = '';
    }
    ElMessage.warning('PDF 文件不能超过 200 MB');
    return;
  }

  uploadFile.value = file;
}

async function submitManual() {
  if (!selectedVehicleId.value || !uploadFile.value) {
    ElMessage.warning('请选择车型和 PDF 文件');
    return;
  }

  uploading.value = true;

  const formData = new FormData();
  formData.append('vehicleId', String(selectedVehicleId.value));
  formData.append('sourceType', 'manual-upload');
  formData.append('sourceUrl', sourceUrl.value);
  formData.append('file', uploadFile.value);

  try {
    await uploadManual(formData, props.adminToken);
    await refreshAll();
    resetUploadForm();
    ElMessage.success('手册已上传，正在生成知识库');
  } catch (error) {
    handleAdminError(error, '上传失败');
  } finally {
    uploading.value = false;
  }
}

async function reprocessManual(manualId: number) {
  const manual = manuals.value.find((item) => item.id === manualId);
  if (!manual || isManualActionDisabled(manual)) {
    return;
  }

  setManualBusy(manualId, true);
  try {
    await reprocessAdminManual(manualId, props.adminToken);
    await refreshAll();
    ElMessage.success('已重新开始解析');
  } catch (error) {
    handleAdminError(error, '重新解析失败');
  } finally {
    setManualBusy(manualId, false);
  }
}

async function deleteManual(manual: Manual) {
  if (isManualActionDisabled(manual)) {
    return;
  }

  try {
    await ElMessageBox.confirm(
      `确认删除“${manual.fileName}”？手册记录和页面图片会一并移除。`,
      '删除手册',
      {
        confirmButtonText: '删除',
        cancelButtonText: '取消',
        type: 'warning'
      }
    );
    setManualBusy(manual.id, true);
    await deleteAdminManual(manual.id, props.adminToken);
    await refreshAll();
    ElMessage.success('手册已删除');
  } catch (error) {
    if (error === 'cancel' || error === 'close') {
      return;
    }

    handleAdminError(error, '删除失败');
  } finally {
    setManualBusy(manual.id, false);
  }
}

function isManualBusy(manualId: number): boolean {
  return busyManualIds.value.has(manualId);
}

function isManualActionDisabled(manual: Manual): boolean {
  return manual.status === 'processing' || isManualBusy(manual.id);
}

function setManualBusy(manualId: number, busy: boolean) {
  const next = new Set(busyManualIds.value);
  if (busy) {
    next.add(manualId);
  } else {
    next.delete(manualId);
  }
  busyManualIds.value = next;
}

function statusText(status: string): string {
  const map: Record<string, string> = {
    uploaded: '等待解析',
    processing: '解析中',
    completed: '可用',
    failed: '处理失败'
  };

  return map[status] ?? status;
}

function resetUploadForm() {
  uploadFile.value = null;
  sourceUrl.value = '';

  if (fileInputRef.value) {
    fileInputRef.value.value = '';
  }
}

function handleAdminError(error: unknown, fallbackMessage: string) {
  if (isUnauthorizedError(error)) {
    ElMessage.error('后台登录已失效');
    emit('logout');
    return;
  }

  ElMessage.error(fallbackMessage);
}

function isUnauthorizedError(error: unknown): boolean {
  return (error as { response?: { status?: number } }).response?.status === 401;
}
</script>
