package com.dotnative.plugins

import android.app.Activity
import android.app.AlertDialog
import android.app.Dialog
import android.content.Context
import android.content.res.Configuration
import android.view.ContextThemeWrapper
import android.widget.LinearLayout
import android.widget.NumberPicker
import java.text.DateFormatSymbols
import java.util.Locale

private var activeWheelTimePicker: Dialog? = null

fun DotNativeWheelTimePickerPlugin(activity: Activity) {
    val channel = NativeChannels.channel("dotnative.wheeltimepicker")
    channel.onReset = { activeWheelTimePicker?.dismiss() }
    channel.handle("show") { args, reply ->
        val map = args as? Map<*, *>
        val initial = (map?.get("initialTime") as? String)?.split(":")
        val interval = (map?.get("minuteInterval") as? Long)?.toInt()
        val is24Hour = map?.get("is24Hour") as? Boolean ?: true
        val title = map?.get("title") as? String
        val confirm = map?.get("confirmText") as? String
        val cancel = map?.get("cancelText") as? String
        val localeTag = map?.get("locale") as? String
        val locale =
            try {
                localeTag?.let { Locale.forLanguageTag(it) }
            } catch (_: Exception) {
                reply.failure("invalid_arguments", "Expected a valid locale tag")
                return@handle
            }
        val hour = initial?.getOrNull(0)?.toIntOrNull()
        val minute = initial?.getOrNull(1)?.toIntOrNull()
        if (
            initial?.size != 2 ||
                hour == null ||
                minute == null ||
                hour !in 0..23 ||
                minute !in 0..59 ||
                interval == null ||
                interval !in 1..30 ||
                60 % interval != 0 ||
                minute % interval != 0 ||
                title == null ||
                confirm == null ||
                cancel == null
        ) {
            reply.failure("invalid_arguments", "Expected valid time picker options")
        } else if (activeWheelTimePicker != null || activity.isFinishing || activity.isDestroyed) {
            reply.failure(
                "picker_unavailable",
                "A picker is already open or the activity is unavailable",
            )
        } else {
            var finished = false
            fun finish(value: String?) {
                if (!finished) {
                    finished = true
                    activeWheelTimePicker = null
                    reply.success(value)
                }
            }
            val context = ContextThemeWrapper(activity, android.R.style.Theme_Holo_Light_Dialog)
            if (locale != null) {
                context.applyOverrideConfiguration(
                    Configuration(activity.resources.configuration).apply { setLocale(locale) },
                )
            }
            val wheels =
                TimeWheels(
                    context,
                    hour,
                    minute,
                    interval,
                    is24Hour,
                    locale ?: context.resources.configuration.locales[0],
                    map?.get("looping") as? Boolean ?: true,
                )
            val dialog =
                AlertDialog.Builder(context)
                    .setTitle(title)
                    .setView(wheels)
                    .setPositiveButton(confirm) { _, _ -> finish(wheels.time()) }
                    .setNegativeButton(cancel) { _, _ -> finish(null) }
                    .create()
            dialog.setOnCancelListener { finish(null) }
            dialog.setOnDismissListener { finish(null) }
            reply.onCancel = { dialog.dismiss() }
            activeWheelTimePicker = dialog
            dialog.show()
        }
    }
    channel.handle("cancel") { _, reply ->
        activeWheelTimePicker?.dismiss()
        reply.success(null)
    }
}

private class TimeWheels(
    context: Context,
    hour: Int,
    minute: Int,
    private val interval: Int,
    private val is24Hour: Boolean,
    private val locale: Locale,
    private val looping: Boolean,
) : LinearLayout(context) {
    private var hourIndex = if (is24Hour) hour else (hour + 11) % 12
    private var minuteIndex = minute / interval
    private var periodIndex = if (hour >= 12) 1 else 0
    private val hours = NumberPicker(context)
    private val minutes = NumberPicker(context)
    private val period = if (is24Hour) null else NumberPicker(context)

    init {
        orientation = HORIZONTAL
        val padding = (16 * resources.displayMetrics.density).toInt()
        setPadding(padding, 0, padding, 0)
        val hourLabels =
            if (is24Hour) (0..23).map { "%02d".format(Locale.ROOT, it) }
            else (1..12).map { "%02d".format(Locale.ROOT, it) }
        val minuteLabels = (0 until 60 step interval).map { "%02d".format(Locale.ROOT, it) }
        addWheel(hours, hourLabels, hourIndex) { hourIndex = it }
        addWheel(minutes, minuteLabels, minuteIndex) { minuteIndex = it }
        val periodNames = DateFormatSymbols(locale).amPmStrings
        val morning = periodNames.getOrNull(0)?.takeIf { it.isNotBlank() } ?: "AM"
        val evening = periodNames.getOrNull(1)?.takeIf { it.isNotBlank() } ?: "PM"
        period?.let {
            addWheel(it, listOf(morning, evening), periodIndex) { index -> periodIndex = index }
        }
    }

    private fun addWheel(
        picker: NumberPicker,
        labels: List<String>,
        value: Int,
        changed: (Int) -> Unit,
    ) {
        picker.descendantFocusability = NumberPicker.FOCUS_BLOCK_DESCENDANTS
        picker.displayedValues = null
        picker.minValue = 0
        picker.maxValue = labels.size - 1
        picker.displayedValues = labels.toTypedArray()
        picker.value = value
        picker.wrapSelectorWheel = looping
        picker.setOnValueChangedListener { _, _, next -> changed(next) }
        addView(picker, LayoutParams(0, LayoutParams.WRAP_CONTENT, 1f))
    }

    fun time(): String {
        val minute = minuteIndex * interval
        val hour =
            if (is24Hour) hourIndex
            else {
                val base = (hourIndex + 1) % 12
                if (periodIndex == 1) base + 12 else base
            }
        return String.format(Locale.ROOT, "%02d:%02d", hour, minute)
    }
}
