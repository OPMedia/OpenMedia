define([],function(){"use strict";
// OpenMedia（去 Emby 化，用户决策）：注册/会员校验体系已移除。
// 桩化语义：validateFeature 恒 resolve（所有功能解锁，enableTimeLimit=false
// 避免 playbackvalidation 启动自动停止计时器）；showPremiereInfo 无操作。
// 消费方：skinmanager(themes)/xmltv/recordingcreator/playbackvalidation/homesections 等。
function validateFeature(feature,options){return Promise.resolve({enableTimeLimit:!1})}
function showPremiereInfo(){return Promise.resolve()}
return{validateFeature:validateFeature,showPremiereInfo:showPremiereInfo}});
