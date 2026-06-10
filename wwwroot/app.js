const { createApp, ref, onMounted, onUnmounted } = Vue;

const app = createApp({
    setup() {
        // 状态
        const status = ref({
            isAccelerating: false,
            enabledServiceCount: 0,
            selectedIpCount: 0,
            selectedIps: {},
            lastSpeedTest: null,
            proxyRunning: false,
            proxyConnections: 0,
            isAdmin: false,
            webPort: 2606,
        });
        
        const services = ref([]);
        const config = ref({
            proxyEnabled: true,
            autoStart: false,
            autoTestIntervalHours: 24,
            setSystemProxy: true,
        });
        const logs = ref([]);
        const testing = ref(false);
        const starting = ref(false);
        const showIps = ref(false);

        // 流量统计
        const traffic = ref({
            totalConnections: 0,
            activeConnections: 0,
            totalBytesReceived: 0,
            totalBytesSent: 0,
            recentLogs: [],
        });

        // HTTP日志显示开关
        const showHttpLogs = ref(false);

        // 失联检测（WebSocket）
        const disconnected = ref(false);
        let ws = null;
        let wsReconnectTimer = null;
        let wsFailCount = 0;
        const MAX_WS_FAILS = 3;

        // 流量轮询
        let trafficTimer = null;

        // API 请求封装
        const api = async (method, path, body = null) => {
            try {
                const options = {
                    method,
                    headers: { 'Content-Type': 'application/json' },
                };
                if (body) {
                    options.body = JSON.stringify(body);
                }
                const response = await fetch(`/api${path}`, options);
                if (!response.ok) {
                    throw new Error(`HTTP ${response.status}`);
                }
                return await response.json();
            } catch (error) {
                console.error(`API Error: ${method} ${path}`, error);
                showToast(`请求失败: ${error.message}`, 'error');
                throw error;
            }
        };

        // 刷新状态
        const refreshStatus = async () => {
            try {
                const data = await api('GET', '/status');
                status.value = data;
            } catch (e) {
                // 静默处理
            }
        };

        // 刷新服务列表
        const refreshServices = async () => {
            try {
                const data = await api('GET', '/services');
                services.value = data;
            } catch (e) {
                // 静默处理
            }
        };

        // 刷新配置
        const refreshConfig = async () => {
            try {
                const data = await api('GET', '/config');
                config.value = {
                    proxyEnabled: data.proxyEnabled,
                    autoStart: data.autoStart,
                    autoTestIntervalHours: data.autoTestIntervalHours,
                    showHttpLogs: data.showHttpLogs || false,
                    setSystemProxy: data.setSystemProxy !== false,
                };
                showHttpLogs.value = config.value.showHttpLogs;
            } catch (e) {
                // 静默处理
            }
        };

        // 刷新流量统计
        const refreshTraffic = async () => {
            if (!status.value.proxyRunning && !status.value.isAccelerating) {
                return;
            }
            try {
                const data = await api('GET', '/stats/traffic');
                if (data) {
                    traffic.value = data;
                }
            } catch (e) {
                // 静默处理
            }
        };

        // WebSocket 连接（替代健康检查轮询）
        const connectWs = () => {
            if (ws && ws.readyState <= WebSocket.OPEN) return;

            const proto = location.protocol === 'https:' ? 'wss:' : 'ws:';
            ws = new WebSocket(`${proto}//${location.host}/ws`);

            ws.onopen = () => {
                wsFailCount = 0;
                if (disconnected.value) {
                    disconnected.value = false;
                    showToast('客户端已重新连接', 'success');
                }
            };

            ws.onmessage = () => {
                // 收到心跳回复，重置失败计数
                wsFailCount = 0;
            };

            ws.onclose = () => {
                wsFailCount++;
                if (wsFailCount >= MAX_WS_FAILS) {
                    disconnected.value = true;
                }
                // 自动重连
                wsReconnectTimer = setTimeout(connectWs, 3000);
            };

            ws.onerror = () => {
                ws.close();
            };
        };

        // 切换加速状态
        const toggleAcceleration = async () => {
            if (status.value.isAccelerating) {
                // 停止加速
                await api('POST', '/stop');
                showToast('加速已停止');
                await refreshStatus();
                await refreshLogs();
                await refreshTraffic();
            } else {
                // 启动加速（带加载状态）
                starting.value = true;
                try {
                    const result = await api('POST', '/start');
                    await refreshStatus();
                    await refreshLogs();
                    await refreshTraffic();

                    if (result.success) {
                        showToast(result.message || '加速已启动', 'success');
                    } else {
                        showToast(result.message || '加速启动失败', 'error');
                    }
                } catch (e) {
                    // api() 已经显示了错误提示，此处无需重复
                } finally {
                    starting.value = false;
                }
            }
        };

        // 运行测速
        const runSpeedTest = async () => {
            testing.value = true;
            try {
                await api('POST', '/speedtest');
                showToast('测速已开始，请稍候...');
                
                // 轮询测速结果
                const pollInterval = setInterval(async () => {
                    await refreshStatus();
                    await refreshLogs();
                    
                    // 如果有新的测速结果，停止轮询
                    if (status.value.lastSpeedTest) {
                        clearInterval(pollInterval);
                        testing.value = false;
                    }
                }, 2000);
                
                // 5分钟后停止轮询
                setTimeout(() => {
                    clearInterval(pollInterval);
                    testing.value = false;
                }, 300000);
            } catch (e) {
                testing.value = false;
            }
        };

        // 切换服务
        const toggleService = async (service) => {
            try {
                await api('POST', `/services/${service.id}/toggle`, {
                    enabled: !service.enabled,
                });
                await refreshServices();
                await refreshStatus();
                showToast(`${service.name} 已${service.enabled ? '禁用' : '启用'}`);
            } catch (e) {
                // 静默处理
            }
        };

        // 全选
        const selectAll = async () => {
            const allEnabled = services.value.every(s => s.enabled);
            for (const service of services.value) {
                if (allEnabled ? service.enabled : !service.enabled) {
                    await api('POST', `/services/${service.id}/toggle`, {
                        enabled: !allEnabled,
                    });
                }
            }
            await refreshServices();
            await refreshStatus();
            showToast(allEnabled ? '已全部禁用' : '已全部启用');
        };

        // 保存配置
        const saveConfig = async () => {
            try {
                await api('POST', '/config', config.value);
                showToast('配置已保存');
            } catch (e) {
                // 静默处理
            }
        };

        // 刷新日志
        const refreshLogs = async () => {
            try {
                const data = await api('GET', '/logs');
                logs.value = data;
            } catch (e) {
                // 静默处理
            }
        };

        // 格式化时间
        const formatTime = (time) => {
            if (!time) return '从未';
            const date = new Date(time);
            const now = new Date();
            const diff = now - date;
            
            if (diff < 60000) return '刚刚';
            if (diff < 3600000) return `${Math.floor(diff / 60000)} 分钟前`;
            if (diff < 86400000) return `${Math.floor(diff / 3600000)} 小时前`;
            return date.toLocaleDateString('zh-CN');
        };

        // 格式化字节数
        const formatBytes = (bytes) => {
            if (!bytes || bytes === 0) return '0 B';
            if (bytes < 1024) return `${bytes} B`;
            if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
            if (bytes < 1024 * 1024 * 1024) return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
            return `${(bytes / (1024 * 1024 * 1024)).toFixed(2)} GB`;
        };

        // Toast 提示
        let toastTimer = null;
        const showToast = (message, type = 'success') => {
            const oldToast = document.querySelector('.toast');
            if (oldToast) oldToast.remove();
            
            const toast = document.createElement('div');
            toast.className = `toast ${type}`;
            toast.textContent = message;
            document.body.appendChild(toast);
            
            if (toastTimer) clearTimeout(toastTimer);
            toastTimer = setTimeout(() => {
                toast.remove();
            }, 3000);
        };

        // 初始化
        onMounted(async () => {
            await refreshStatus();
            await refreshServices();
            await refreshConfig();
            await refreshLogs();
            await refreshTraffic();
            
            // 同步HTTP日志显示设置
            showHttpLogs.value = config.value.showHttpLogs || false;

            // 定期刷新状态
            setInterval(async () => {
                await refreshStatus();
            }, 5000);

            // 定期刷新流量
            trafficTimer = setInterval(async () => {
                await refreshTraffic();
            }, 3000);

            // WebSocket 连接（替代每10秒的健康检查轮询）
            connectWs();
        });

        onUnmounted(() => {
            if (wsReconnectTimer) clearTimeout(wsReconnectTimer);
            if (ws) ws.close();
            if (trafficTimer) clearInterval(trafficTimer);
        });

        // 切换HTTP日志显示
        const toggleHttpLogs = async () => {
            showHttpLogs.value = !showHttpLogs.value;
            await api('POST', '/config', { showHttpLogs: showHttpLogs.value });
            await refreshTraffic();
        };

        return {
            status,
            services,
            config,
            logs,
            testing,
            starting,
            showIps,
            traffic,
            disconnected,
            showHttpLogs,
            toggleAcceleration,
            runSpeedTest,
            toggleService,
            selectAll,
            saveConfig,
            refreshLogs,
            toggleHttpLogs,
            formatTime,
            formatBytes,
        };
    }
});

app.mount('#app');
