<template>
  <div class="chat-shell" :class="{ 'sidebar-open': mobileSidebarOpen }">
    <aside class="chat-sidebar">
      <div class="sidebar-brand">
        <div class="brand-mark compact">
          <CarFront :size="20" />
        </div>
        <div>
          <strong>手册助手</strong>
          <span>MANUAL AI</span>
        </div>
        <button
          class="icon-button mobile-sidebar-close"
          type="button"
          aria-label="关闭侧栏"
          @click="closeMobileSidebar"
        >
          <X :size="18" />
        </button>
      </div>

      <button class="new-chat-button" type="button" @click="newChat">
        <SquarePen :size="17" />
        <span>新对话</span>
      </button>

      <section class="sidebar-section vehicle-section">
        <div class="sidebar-label">
          <span>当前车辆</span>
          <span class="manual-online" :class="{ offline: loadError }">
            <i /> {{ connectionLabel }}
          </span>
        </div>
        <el-select
          v-model="selectedVehicleId"
          class="vehicle-select"
          placeholder="选择车型"
          @change="handleVehicleChange"
        >
          <el-option
            v-for="vehicle in vehicles"
            :key="vehicle.id"
            :label="formatVehicle(vehicle)"
            :value="vehicle.id"
          />
        </el-select>
      </section>

      <section class="sidebar-section history-section">
        <div class="sidebar-label">
          <span>最近查询</span>
          <span>{{ historyConversations.length }}</span>
        </div>
        <div class="history-list">
          <button
            v-for="item in historyConversations"
            :key="historyConversationKey(item)"
            class="conversation-item"
            :class="{ active: activeHistoryKey === historyConversationKey(item) }"
            type="button"
            @click="restoreHistory(historyConversationKey(item))"
          >
            <MessageSquare :size="15" />
            <span>{{ item.question }}</span>
            <time>{{ formatHistoryTime(item.createTime) }}</time>
          </button>
          <div v-if="!loadingData && historyConversations.length === 0" class="history-empty">
            <Clock3 :size="17" />
            <span>查询记录会显示在这里</span>
          </div>
        </div>
      </section>

      <button class="sidebar-footer-link" type="button" @click="goAdminFromSidebar">
        <ShieldCheck :size="16" />
        <span>管理后台</span>
        <ChevronRight :size="15" />
      </button>
    </aside>

    <button
      class="mobile-sidebar-backdrop"
      :class="{ visible: mobileSidebarOpen }"
      type="button"
      aria-label="关闭侧栏"
      @click="closeMobileSidebar"
    />

    <main class="chat-main">
      <header class="chat-header">
        <button
          class="icon-button mobile-menu-button"
          type="button"
          :aria-expanded="mobileSidebarOpen"
          aria-label="打开侧栏"
          @click="openMobileSidebar"
        >
          <Menu :size="20" />
        </button>
        <div class="header-vehicle">
          <span class="header-kicker">{{ currentVehicle?.brand ?? '汽车' }}用户手册</span>
          <h1>{{ currentVehicleTitle }}</h1>
          <p>{{ currentVehicleDetail }}</p>
        </div>
        <div class="header-actions">
          <el-button
            class="manual-browser-button"
            :disabled="!currentManual || loadingData"
            @click="openManualBrowser"
          >
            <BookOpen :size="16" />
            浏览手册
          </el-button>
          <div
            class="header-status"
            :class="{ disconnected: loadError }"
            :title="headerStatusText"
          >
            <WifiOff v-if="loadError" :size="17" />
            <LoaderCircle v-else-if="loadingData" :size="17" />
            <BookCheck v-else :size="17" />
            <span>{{ headerStatusText }}</span>
          </div>
        </div>
      </header>

      <div ref="messageListRef" class="chat-messages" aria-live="polite">
        <section v-if="messages.length === 0" class="empty-chat">
          <div class="empty-chat-mark">
            <LoaderCircle v-if="loadingData" class="state-spinner" :size="24" />
            <WifiOff v-else-if="loadError" :size="24" />
            <BookOpenText v-else :size="24" />
          </div>
          <div class="empty-chat-copy">
            <span v-if="loadingData">正在建立手册连接</span>
            <span v-else-if="loadError">连接未完成</span>
            <span v-else>{{ currentVehicle?.model ?? '当前车型' }} · 手册问答</span>
            <h2 v-if="loadingData">正在载入车型与手册</h2>
            <h2 v-else-if="loadError">暂时无法读取手册数据</h2>
            <h2 v-else>关于这辆车，想先查什么？</h2>
          </div>
          <div v-if="loadError" class="empty-state-action">
            <p>请确认后端服务可用，然后重新加载。</p>
            <el-button :loading="loadingData" @click="initializeUser">
              <RotateCw :size="15" />
              重新加载
            </el-button>
          </div>
          <div v-else-if="!loadingData" class="prompt-list">
            <button
              type="button"
              :disabled="asking"
              @click="submitPrompt('PDA 是什么？')"
            >
              <CircleGauge :size="20" />
              <span>
                <small>驾驶辅助</small>
                PDA 是什么？
              </span>
              <ArrowUpRight :size="17" />
            </button>
            <button
              type="button"
              :disabled="asking"
              @click="submitPrompt('PDA 会提供哪些辅助？')"
            >
              <ShieldCheck :size="20" />
              <span>
                <small>辅助能力</small>
                PDA 会提供哪些辅助？
              </span>
              <ArrowUpRight :size="17" />
            </button>
            <button
              type="button"
              :disabled="asking"
              @click="submitPrompt('高速跑 2000 公里需要注意什么？')"
            >
              <Route :size="20" />
              <span>
                <small>长途驾驶</small>
                出发前需要检查什么？
              </span>
              <ArrowUpRight :size="17" />
            </button>
          </div>
        </section>

        <div v-else class="message-thread">
          <article
            v-for="message in messages"
            :key="message.id"
            class="chat-message"
            :class="[message.role, { pending: message.pending, error: message.error }]"
          >
            <div class="message-avatar">
              <UserRound v-if="message.role === 'user'" :size="17" />
              <Bot v-else :size="18" />
            </div>

            <div class="message-body">
              <div class="message-author">
                <strong>{{ message.role === 'user' ? '你' : '手册助手' }}</strong>
                <span
                  v-if="message.role === 'assistant' && !message.pending && !message.error"
                  :class="{ unverified: !message.references?.length }"
                >
                  {{ message.references?.length ? '基于已导入手册' : '未找到手册依据' }}
                </span>
                <el-tooltip
                  v-if="message.role === 'assistant' && !message.pending && !message.error"
                  :content="copiedMessageId === message.id ? '已复制' : '复制回答'"
                  placement="top"
                >
                  <button
                    class="message-copy-button"
                    type="button"
                    :aria-label="copiedMessageId === message.id ? '回答已复制' : '复制回答'"
                    @click="copyAnswer(message)"
                  >
                    <Check v-if="copiedMessageId === message.id" :size="14" />
                    <Copy v-else :size="14" />
                  </button>
                </el-tooltip>
              </div>

              <div v-if="message.pending" class="answer-loading">
                <LoaderCircle :size="17" />
                <span>正在定位章节和页码</span>
              </div>
              <div
                v-else-if="message.role === 'assistant' && !message.error"
                class="markdown-answer"
                v-html="renderMarkdown(message.content)"
              />
              <p v-else>{{ message.content }}</p>

              <button
                v-if="message.error"
                class="retry-button"
                type="button"
                :disabled="asking"
                @click="retryAnswer(message)"
              >
                <RotateCw :size="15" />
                重新查询
              </button>

              <section
                v-if="message.role === 'assistant' && message.references?.length"
                class="source-list"
              >
                <div class="source-heading">
                  <div>
                    <BookMarked :size="17" />
                    <strong>手册依据</strong>
                  </div>
                  <span>{{ message.references.length }} 处原文</span>
                </div>

                <article
                  v-for="source in message.references"
                  :key="`${source.documentId}-${source.pdfPageNumber}`"
                  class="source-card"
                >
                  <button
                    class="source-page"
                    type="button"
                    :aria-label="`查看${source.documentName}第${sourcePage(source)}页整页图片`"
                    @click="previewSource(source, message.references)"
                  >
                    <img
                      v-if="!hasSourceImageError(source)"
                      :src="assetUrl(source.pageImageUrl)"
                      :alt="`${source.documentName} 第 ${source.pdfPageNumber} 页`"
                      loading="lazy"
                      decoding="async"
                      @error="markSourceImageError(source)"
                    />
                    <div v-else class="source-page-error">
                      <FileWarning :size="24" />
                      <b>整页图片加载失败</b>
                    </div>
                    <span>第 {{ sourcePage(source) }} 页</span>
                    <i><Maximize2 :size="15" /></i>
                  </button>

                  <div class="source-meta">
                    <div class="source-title">
                      <span>{{ source.chapter }}</span>
                      <strong>{{ source.documentName }}</strong>
                    </div>
                    <blockquote>{{ source.quote }}</blockquote>
                    <div class="source-actions">
                      <el-button @click="previewSource(source, message.references)">
                        <Search :size="16" />
                        查看整页
                      </el-button>
                      <el-tooltip
                        v-if="source.pdfPageUrl"
                        content="在新窗口打开 PDF"
                        placement="top"
                      >
                        <el-button
                          circle
                          aria-label="打开 PDF"
                          @click="openPdfSource(source)"
                        >
                          <ExternalLink :size="16" />
                        </el-button>
                      </el-tooltip>
                    </div>
                  </div>
                </article>
              </section>
            </div>
          </article>
        </div>
      </div>

      <div class="composer-dock">
        <form class="chat-composer" @submit.prevent="sendQuestion">
          <el-input
            ref="questionInputRef"
            v-model="question"
            :autosize="{ minRows: 1, maxRows: 5 }"
            :disabled="!selectedVehicleId || loadingData || Boolean(loadError)"
            maxlength="1000"
            resize="none"
            type="textarea"
            placeholder="例如：PDA 是什么？"
            aria-label="向手册助手提问"
            @keydown.enter.exact="handleComposerEnter"
          />
          <el-button
            circle
            native-type="submit"
            :disabled="!question.trim() || !selectedVehicleId || asking || loadingData || Boolean(loadError)"
            :loading="asking"
            aria-label="发送问题"
          >
            <Send :size="18" />
          </el-button>
        </form>
        <div class="composer-context">
          <span><LockKeyhole :size="13" /> 仅检索当前车型手册</span>
          <span>{{ currentVehicle?.year }} {{ currentVehicle?.model }}</span>
        </div>
      </div>
    </main>

    <el-dialog
      v-model="previewVisible"
      class="page-dialog"
      width="min(1040px, 96vw)"
      destroy-on-close
    >
      <template #header>
        <div class="dialog-title">
          <div>
            <span>手册原页</span>
            <strong>{{ previewTitle }}</strong>
          </div>
          <div class="zoom-controls">
            <el-tooltip content="上一页" placement="bottom">
              <el-button
                circle
                aria-label="查看上一页"
                :disabled="!hasPreviousManualPage"
                @click="showPreviousManualPage"
              >
                <ChevronLeft :size="16" />
              </el-button>
            </el-tooltip>
            <el-tooltip content="下一页" placement="bottom">
              <el-button
                circle
                aria-label="查看下一页"
                :disabled="!hasNextManualPage"
                @click="showNextManualPage"
              >
                <ChevronRight :size="16" />
              </el-button>
            </el-tooltip>
            <i class="zoom-divider" />
            <el-tooltip class="citation-control" content="上一处引用" placement="bottom">
              <el-button
                circle
                aria-label="查看上一处引用"
                :disabled="!hasPreviousSource"
                @click="showPreviousSource"
              >
                <ChevronsLeft :size="16" />
              </el-button>
            </el-tooltip>
            <el-tooltip class="citation-control" content="下一处引用" placement="bottom">
              <el-button
                circle
                aria-label="查看下一处引用"
                :disabled="!hasNextSource"
                @click="showNextSource"
              >
                <ChevronsRight :size="16" />
              </el-button>
            </el-tooltip>
            <i class="zoom-divider" />
            <el-tooltip content="缩小" placement="bottom">
              <el-button circle aria-label="缩小页面" @click="zoomOut">
                <ZoomOut :size="16" />
              </el-button>
            </el-tooltip>
            <button class="zoom-value" type="button" @click="resetZoom">
              {{ Math.round(previewScale * 100) }}%
            </button>
            <el-tooltip content="放大" placement="bottom">
              <el-button circle aria-label="放大页面" @click="zoomIn">
                <ZoomIn :size="16" />
              </el-button>
            </el-tooltip>
            <el-tooltip
              v-if="activeSource?.pdfPageUrl"
              content="打开 PDF"
              placement="bottom"
            >
              <el-button
                circle
                aria-label="打开 PDF"
                @click="openPdfSource(activeSource)"
              >
                <ExternalLink :size="16" />
              </el-button>
            </el-tooltip>
          </div>
        </div>
      </template>
      <div class="page-preview">
        <img
          v-if="activeSource && !hasSourceImageError(activeSource)"
          :src="assetUrl(activeSource.pageImageUrl)"
          :alt="previewTitle"
          :style="{ width: `${previewScale * 100}%` }"
          @error="markSourceImageError(activeSource)"
        />
        <div v-else-if="activeSource" class="page-preview-error">
          <FileWarning :size="28" />
          <strong>整页图片暂时无法显示</strong>
          <span>可以稍后重试，或打开原始 PDF 查看对应页面。</span>
          <el-button v-if="activeSource.pdfPageUrl" @click="openPdfSource(activeSource)">
            <ExternalLink :size="15" />
            打开 PDF
          </el-button>
        </div>
      </div>
    </el-dialog>

    <el-dialog
      v-model="manualBrowserVisible"
      class="manual-browser-dialog"
      width="min(1180px, 96vw)"
      destroy-on-close
    >
      <template #header>
        <div class="manual-browser-title">
          <span>用户手册浏览</span>
          <strong>{{ currentManual?.fileName }}</strong>
        </div>
      </template>
      <div v-if="currentManual" class="manual-browser">
        <aside class="manual-navigation">
          <div class="manual-jump">
            <label for="manual-page-input">跳转 PDF 页</label>
            <div>
              <el-input
                id="manual-page-input"
                v-model="manualPageInput"
                inputmode="numeric"
                :maxlength="String(manualTotalPages).length"
                @keyup.enter="jumpToManualPage"
              />
              <el-button type="primary" @click="jumpToManualPage">前往</el-button>
            </div>
            <small>共 {{ manualTotalPages }} 页</small>
          </div>

          <div class="manual-page-actions">
            <el-button :disabled="manualBrowserPage <= 1" @click="moveManualPage(-1)">
              <ChevronLeft :size="16" />
              上一页
            </el-button>
            <el-button
              :disabled="manualBrowserPage >= manualTotalPages"
              @click="moveManualPage(1)"
            >
              下一页
              <ChevronRight :size="16" />
            </el-button>
          </div>

          <section class="manual-directory">
            <div class="manual-directory-heading">
              <BookMarked :size="16" />
              <strong>目录与索引</strong>
            </div>
            <p v-if="manualDirectoryPages.length === 0">当前手册未提供可识别目录，可直接跳转页码。</p>
            <button
              v-for="page in manualDirectoryPages"
              :key="page.pdfPageNumber"
              type="button"
              :class="{ active: page.pdfPageNumber === manualBrowserPage }"
              @click="setManualBrowserPage(page.pdfPageNumber)"
            >
              <span>{{ manualDirectoryLabel(page) }}</span>
              <b>PDF {{ page.pdfPageNumber }}</b>
            </button>
          </section>
        </aside>

        <section class="manual-document">
          <div class="manual-document-meta">
            <span>PDF 第 {{ manualBrowserPage }} 页</span>
            <strong v-if="currentManualPage?.printedPageNumber">手册 P.{{ currentManualPage.printedPageNumber }}</strong>
            <strong v-else>原页预览</strong>
          </div>
          <div class="manual-browser-preview">
            <img
              v-if="!manualBrowserImageError"
              :src="assetUrl(manualBrowserImageUrl)"
              :alt="`${currentManual.fileName} PDF 第 ${manualBrowserPage} 页`"
              @error="manualBrowserImageError = true"
            />
            <div v-else class="manual-browser-error">
              <FileWarning :size="26" />
              <strong>这一页的预览图片不可用</strong>
              <el-button @click="openCurrentManualPdf">
                <ExternalLink :size="15" />
                在 PDF 中查看
              </el-button>
            </div>
          </div>
        </section>
      </div>
    </el-dialog>
  </div>
</template>

<script setup lang="ts">
import { computed, nextTick, onMounted, ref } from 'vue';
import { ElMessage } from 'element-plus';
import {
  ArrowUpRight,
  BookCheck,
  BookMarked,
  BookOpen,
  BookOpenText,
  Bot,
  CarFront,
  Check,
  ChevronLeft,
  ChevronRight,
  ChevronsLeft,
  ChevronsRight,
  CircleGauge,
  Clock3,
  Copy,
  ExternalLink,
  FileWarning,
  LoaderCircle,
  LockKeyhole,
  Maximize2,
  Menu,
  MessageSquare,
  RotateCw,
  Route,
  Search,
  Send,
  ShieldCheck,
  SquarePen,
  UserRound,
  WifiOff,
  X,
  ZoomIn,
  ZoomOut
} from 'lucide-vue-next';
import {
  askManual,
  assetUrl,
  getHistory,
  getManualManifest,
  getVehicleManual,
  getVehicles,
  login
} from '../api';
import { renderMarkdown } from '../markdown';
import type {
  ChatHistory,
  ChatMessage,
  KnowledgeReference,
  ManualManifestPage,
  UserManual,
  Vehicle
} from '../types';

const emit = defineEmits<{
  goAdmin: [];
}>();

function createClientId(): string {
  if (typeof globalThis.crypto?.randomUUID === 'function') {
    return globalThis.crypto.randomUUID();
  }

  return `client-${Date.now().toString(36)}-${Math.random().toString(36).slice(2)}`;
}

const userId = ref(1);
const vehicles = ref<Vehicle[]>([]);
const history = ref<ChatHistory[]>([]);
const messages = ref<ChatMessage[]>([]);
const selectedVehicleId = ref<number>();
const activeConversationId = ref<string>(createClientId());
const activeHistoryKey = ref<string | null>(null);
const question = ref('');
const asking = ref(false);
const loadingData = ref(true);
const loadError = ref('');
const mobileSidebarOpen = ref(false);
const messageListRef = ref<HTMLElement | null>(null);
const questionInputRef = ref<{ focus: () => void } | null>(null);
const previewVisible = ref(false);
const activeSource = ref<KnowledgeReference | null>(null);
const previewSources = ref<KnowledgeReference[]>([]);
const activeSourceIndex = ref(0);
const previewScale = ref(0.5);
const copiedMessageId = ref<string | null>(null);
const sourceImageErrors = ref<Record<string, boolean>>({});
const currentManual = ref<UserManual | null>(null);
const manualManifestPages = ref<ManualManifestPage[]>([]);
const manualBrowserVisible = ref(false);
const manualBrowserPage = ref(1);
const manualPageInput = ref('1');
const manualBrowserImageError = ref(false);

const historyConversations = computed(() => {
  const seen = new Set<string>();

  return history.value.filter((item) => {
    const key = historyConversationKey(item);
    if (seen.has(key)) {
      return false;
    }

    seen.add(key);
    return true;
  });
});

const connectionLabel = computed(() => {
  if (loadingData.value) {
    return '连接中';
  }

  return loadError.value ? '连接失败' : '已匹配手册';
});

const headerStatusText = computed(() => {
  if (loadingData.value) {
    return '正在连接手册';
  }

  return loadError.value ? '手册连接失败' : '手册已连接';
});

const currentVehicle = computed(() => {
  return vehicles.value.find((vehicle) => vehicle.id === selectedVehicleId.value);
});

const currentVehicleTitle = computed(() => {
  const vehicle = currentVehicle.value;
  return vehicle ? `${vehicle.year} ${vehicle.brand}${vehicle.model}` : '请选择车型';
});

const currentVehicleDetail = computed(() => {
  const vehicle = currentVehicle.value;
  return vehicle ? `${vehicle.engine} · ${vehicle.configuration}` : '选择车辆后开始查询';
});

const previewTitle = computed(() => {
  if (!activeSource.value) {
    return '';
  }

  return `${activeSource.value.documentName} · 第 ${sourcePage(activeSource.value)} 页`;
});

const hasPreviousSource = computed(() => activeSourceIndex.value > 0);
const hasNextSource = computed(() => {
  return activeSourceIndex.value < previewSources.value.length - 1;
});
const hasPreviousManualPage = computed(() => {
  return Boolean(activeSource.value && activeSource.value.pdfPageNumber > 1);
});
const hasNextManualPage = computed(() => {
  return Boolean(
    activeSource.value &&
      activeSource.value.totalPages &&
      activeSource.value.pdfPageNumber < activeSource.value.totalPages
  );
});
const manualTotalPages = computed(() => {
  return currentManual.value?.totalPages || manualManifestPages.value.length || 1;
});
const currentManualPage = computed(() => {
  return manualManifestPages.value.find(
    (page) => page.pdfPageNumber === manualBrowserPage.value
  );
});
const manualBrowserImageUrl = computed(() => {
  return currentManualPage.value?.pageImageUrl
    || `/manuals/${currentManual.value?.id}/pages/${manualBrowserPage.value}.webp`;
});
const manualDirectoryPages = computed(() => {
  return manualManifestPages.value.filter((page) => {
    const text = page.pageText || '';
    const dotLinks = (text.match(/\.{3,}/g) || []).length;
    return page.pdfPageNumber <= 60 && (
      text.includes('图片索引')
      || text.includes('字母索引')
      || text.includes('目录')
      || dotLinks >= 3
    );
  });
});

onMounted(() => {
  void initializeUser();
});

async function initializeUser() {
  loadingData.value = true;
  loadError.value = '';

  try {
    const session = await login('demo');
    userId.value = session.userId;
    vehicles.value = await getVehicles();
    selectedVehicleId.value = vehicles.value[0]?.id;
    if (!selectedVehicleId.value) {
      throw new Error('没有可用车型');
    }
    await Promise.all([refreshHistory(), loadCurrentManual()]);
  } catch {
    loadError.value = '车辆和手册数据加载失败';
    ElMessage.error('车辆和手册数据加载失败');
  } finally {
    loadingData.value = false;
  }
}

function formatVehicle(vehicle: Vehicle): string {
  return `${vehicle.year} ${vehicle.brand}${vehicle.model} ${vehicle.engine} ${vehicle.configuration}`;
}

function sourcePage(source: KnowledgeReference): number {
  return source.printedPageNumber ?? source.pdfPageNumber;
}

function formatHistoryTime(value: string): string {
  const date = new Date(value);
  const now = new Date();

  if (date.toDateString() === now.toDateString()) {
    return date.toLocaleTimeString('zh-CN', { hour: '2-digit', minute: '2-digit' });
  }

  return date.toLocaleDateString('zh-CN', { month: 'numeric', day: 'numeric' });
}

async function newChat() {
  messages.value = [];
  activeConversationId.value = createClientId();
  activeHistoryKey.value = null;
  question.value = '';
  closeMobileSidebar();
  await nextTick();
  questionInputRef.value?.focus();
}

async function handleVehicleChange() {
  await newChat();
  await Promise.all([refreshHistory(), loadCurrentManual()]);
}

async function loadCurrentManual() {
  const vehicleId = selectedVehicleId.value;
  currentManual.value = null;
  manualManifestPages.value = [];

  if (!vehicleId) {
    return;
  }

  try {
    const manual = await getVehicleManual(vehicleId);
    if (selectedVehicleId.value !== vehicleId) {
      return;
    }

    currentManual.value = manual;
    const manifest = await getManualManifest(manual.id);
    if (selectedVehicleId.value === vehicleId && currentManual.value?.id === manual.id) {
      manualManifestPages.value = manifest.pages || [];
    }
  } catch {
    // 浏览手册不可用不影响聊天主流程；按钮会保持禁用状态。
  }
}

function openManualBrowser() {
  if (!currentManual.value) {
    ElMessage.warning('当前车型没有可浏览的已解析手册');
    return;
  }

  const directoryStartPage = manualDirectoryPages.value[0]?.pdfPageNumber || 1;
  setManualBrowserPage(directoryStartPage);
  manualBrowserVisible.value = true;
}

function setManualBrowserPage(pageNumber: number) {
  const validPage = Math.min(Math.max(Math.trunc(pageNumber), 1), manualTotalPages.value);
  manualBrowserPage.value = validPage;
  manualPageInput.value = String(validPage);
  manualBrowserImageError.value = false;
}

function jumpToManualPage() {
  const pageNumber = Number.parseInt(manualPageInput.value, 10);
  if (!Number.isInteger(pageNumber) || pageNumber < 1 || pageNumber > manualTotalPages.value) {
    ElMessage.warning(`请输入 1 到 ${manualTotalPages.value} 之间的 PDF 页码`);
    manualPageInput.value = String(manualBrowserPage.value);
    return;
  }

  setManualBrowserPage(pageNumber);
}

function moveManualPage(offset: number) {
  setManualBrowserPage(manualBrowserPage.value + offset);
}

function manualDirectoryLabel(page: ManualManifestPage): string {
  const text = page.pageText || '';
  if (text.includes('图片索引')) {
    return '图片索引';
  }
  if (text.includes('字母索引')) {
    return '字母索引';
  }
  return '目录';
}

function openCurrentManualPdf() {
  if (!currentManual.value?.pdfUrl) {
    return;
  }

  window.open(
    assetUrl(`${currentManual.value.pdfUrl}#page=${manualBrowserPage.value}`),
    '_blank',
    'noopener,noreferrer'
  );
}

async function refreshHistory(throwOnError = false) {
  if (!selectedVehicleId.value) {
    history.value = [];
    return;
  }

  try {
    history.value = await getHistory(userId.value, selectedVehicleId.value);
  } catch (error) {
    if (throwOnError) {
      throw error;
    }

    ElMessage.warning('回答已生成，但历史记录暂时无法刷新');
  }
}

async function submitPrompt(value: string) {
  question.value = value;
  await sendQuestion();
}

async function sendQuestion() {
  const text = question.value.trim();
  if (!text || asking.value || !selectedVehicleId.value) {
    return;
  }

  question.value = '';
  const conversationId = activeConversationId.value;
  activeHistoryKey.value = conversationId;
  messages.value.push({
    id: createClientId(),
    role: 'user',
    content: text
  });

  const pendingMessageId = createClientId();
  messages.value.push({
    id: pendingMessageId,
    role: 'assistant',
    content: '',
    pending: true,
    retryQuestion: text
  });

  await executeQuestion(pendingMessageId, text, conversationId);
}

async function executeQuestion(messageId: string, text: string, conversationId: string) {
  if (!selectedVehicleId.value) {
    return;
  }

  const vehicleId = selectedVehicleId.value;
  asking.value = true;
  await scrollToBottom();

  try {
    const response = await askManual(userId.value, vehicleId, text, conversationId);
    updatePendingMessage(messageId, response.answer, response.references);
    if (selectedVehicleId.value === vehicleId) {
      await refreshHistory();
    }
  } catch {
    updatePendingMessage(
      messageId,
      '暂时没有查到答案，请检查网络后重新查询。',
      [],
      true,
      text
    );
    ElMessage.error('问答服务暂时不可用');
  } finally {
    asking.value = false;
    await scrollToBottom();
  }
}

async function retryAnswer(message: ChatMessage) {
  if (!message.retryQuestion || asking.value) {
    return;
  }

  message.content = '';
  message.error = false;
  message.pending = true;
  await executeQuestion(message.id, message.retryQuestion, activeConversationId.value);
}

function historyConversationKey(item: ChatHistory): string {
  return item.conversationId || `history-${item.id}`;
}

function restoreHistory(conversationKey: string) {
  const entries = history.value
    .filter((entry) => historyConversationKey(entry) === conversationKey)
    .sort((left, right) => Date.parse(left.createTime) - Date.parse(right.createTime));

  if (entries.length === 0) {
    return;
  }

  activeHistoryKey.value = conversationKey;
  activeConversationId.value = entries[0].conversationId || createClientId();
  messages.value = entries.flatMap((item) => [
    { id: `history-${item.id}-user`, role: 'user' as const, content: item.question },
    {
      id: `history-${item.id}-assistant`,
      role: 'assistant' as const,
      content: item.answer,
      references: item.references
    }
  ]);
  closeMobileSidebar();
  void scrollToBottom();
}

function openMobileSidebar() {
  mobileSidebarOpen.value = true;
}

function closeMobileSidebar() {
  mobileSidebarOpen.value = false;
}

function goAdminFromSidebar() {
  closeMobileSidebar();
  emit('goAdmin');
}

function updatePendingMessage(
  messageId: string,
  content: string,
  references: KnowledgeReference[],
  error = false,
  retryQuestion?: string
) {
  const index = messages.value.findIndex((message) => message.id === messageId);
  if (index === -1) {
    return;
  }

  messages.value[index] = {
    id: messageId,
    role: 'assistant',
    content,
    references,
    error,
    retryQuestion
  };
}

function previewSource(source: KnowledgeReference, sources: KnowledgeReference[] = [source]) {
  const imageKey = sourceImageKey(source);
  if (sourceImageErrors.value[imageKey]) {
    const nextErrors = { ...sourceImageErrors.value };
    delete nextErrors[imageKey];
    sourceImageErrors.value = nextErrors;
  }

  activeSource.value = source;
  previewSources.value = sources;
  activeSourceIndex.value = Math.max(0, sources.indexOf(source));
  previewScale.value = 0.5;
  previewVisible.value = true;
}

function showPreviousSource() {
  if (!hasPreviousSource.value) {
    return;
  }

  activeSourceIndex.value -= 1;
  activeSource.value = previewSources.value[activeSourceIndex.value];
}

function showPreviousManualPage() {
  if (!activeSource.value || !hasPreviousManualPage.value) {
    return;
  }

  setActivePreviewPage(activeSource.value.pdfPageNumber - 1);
}

function showNextSource() {
  if (!hasNextSource.value) {
    return;
  }

  activeSourceIndex.value += 1;
  activeSource.value = previewSources.value[activeSourceIndex.value];
}

function showNextManualPage() {
  if (!activeSource.value || !hasNextManualPage.value) {
    return;
  }

  setActivePreviewPage(activeSource.value.pdfPageNumber + 1);
}

function setActivePreviewPage(pageNumber: number) {
  if (!activeSource.value) {
    return;
  }

  activeSource.value = {
    ...activeSource.value,
    pdfPageNumber: pageNumber,
    printedPageNumber: null,
    pageImageUrl: activeSource.value.pageImageUrl.replace(
      /(\d+)(\.[^/]+)$/,
      `${pageNumber}$2`
    ),
    pdfPageUrl: activeSource.value.pdfPageUrl.replace(
      /#page=\d+$/,
      `#page=${pageNumber}`
    )
  };
}

function openPdfSource(source: KnowledgeReference) {
  if (!source.pdfPageUrl) {
    ElMessage.warning('当前演示手册没有可打开的原始 PDF');
    return;
  }

  window.open(assetUrl(source.pdfPageUrl), '_blank', 'noopener,noreferrer');
}

function sourceImageKey(source: KnowledgeReference): string {
  return `${source.documentId}-${source.pdfPageNumber}`;
}

function hasSourceImageError(source: KnowledgeReference): boolean {
  return Boolean(sourceImageErrors.value[sourceImageKey(source)]);
}

function markSourceImageError(source: KnowledgeReference) {
  sourceImageErrors.value = {
    ...sourceImageErrors.value,
    [sourceImageKey(source)]: true
  };
}

async function copyAnswer(message: ChatMessage) {
  try {
    await navigator.clipboard.writeText(message.content);
    copiedMessageId.value = message.id;
    window.setTimeout(() => {
      if (copiedMessageId.value === message.id) {
        copiedMessageId.value = null;
      }
    }, 1800);
  } catch {
    ElMessage.error('复制失败，请手动选择回答内容');
  }
}

function handleComposerEnter(event: KeyboardEvent) {
  if (event.isComposing) {
    return;
  }

  event.preventDefault();
  void sendQuestion();
}

function zoomIn() {
  previewScale.value = Math.min(1.8, Number((previewScale.value + 0.1).toFixed(1)));
}

function zoomOut() {
  previewScale.value = Math.max(0.3, Number((previewScale.value - 0.1).toFixed(1)));
}

function resetZoom() {
  previewScale.value = 0.5;
}

async function scrollToBottom() {
  await nextTick();
  messageListRef.value?.scrollTo({
    top: messageListRef.value.scrollHeight,
    behavior: 'smooth'
  });
}
</script>
